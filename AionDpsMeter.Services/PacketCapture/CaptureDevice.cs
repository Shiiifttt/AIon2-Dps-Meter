using SharpPcap;
using PacketDotNet;
using System.Collections.Concurrent;
using System.Buffers;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.Services.PacketCapture
{
    /// <summary>
    /// Captures game traffic across all network adapters with loopback preference.
    ///
    /// Detection flow:
    ///   1. All eligible adapters open simultaneously in detection mode.
    ///   2. Game keepalives (<see cref="KeepAliveSignature"/>: header plus a current server timestamp)
    ///      are tallied per adapter+port.
    ///   3. When any adapter+port crosses DetectionThreshold a 2-second grace
    ///      window starts so that VPN loopback traffic has time to appear.
    ///   4. After the window, priority rules pick the winning adapter:
    ///      loopback > physical. The winner's device is locked; all others close.
    ///   5. A watchdog resets everything back to detection if no keepalive is
    ///      seen on the locked adapter for WatchdogTimeoutMs.
    ///   6. While searching, adapters that appeared after start (a VPN connecting
    ///      after the meter started with Windows) are picked up every AdapterRefreshMs.
    /// </summary>
    public sealed class CaptureDevice : IPacketCaptureDevice
    {
        private const int KernelBufferSize = 64 * 1024 * 1024;
        private const int ReadTimeoutMs = 100;
        private const int DetectionThreshold = 5;
        private const int GraceWindowMs = 2000;
        // Loading screens can pause keepalives for a minute or more (up to ~170 s seen in a 3 h log);
        // a reset there costs little, as the next keepalives lock again within a couple of seconds.
        private const int WatchdogTimeoutMs = 90_000;
        private const int AdapterRefreshMs = 30_000;
        // ────────────────────────────────────────────────────────────────────────

        private readonly List<AdapterContext> allAdapters = new();

        /// <summary>
        /// Hit counts: adapterName → (port → hitCount).
        /// Written from multiple capture callbacks; guarded by detectionLock.
        /// </summary>
        private readonly Dictionary<string, Dictionary<int, int>> detectionHits = new();

        private readonly object detectionLock = new();

        private volatile bool graceWindowActive;
        private Timer? graceWindowTimer;

        private volatile bool isLocked;       // true once grace window resolved
        private AdapterContext? lockedAdapter;
        private volatile int lockedPort;

        private readonly BlockingCollection<PooledRawPacket> packetQueue;
        private Thread? processingThread;
        private readonly CancellationTokenSource cts = new();

        private Timer? watchdogTimer;
        private volatile int watchdogHeartbeatsSinceCheck;   // keepalives seen on the locked flow since the last check
        private Timer? adapterRefreshTimer;

        private volatile bool isCapturing;
        private volatile int appDroppedPackets;
        private Timer? statsTimer;

        private readonly Dictionary<string, uint> expectedSeqNumbers = new();
        private readonly ILogger<CaptureDevice> logger;
        private readonly TcpStreamBuffer tcpStreamBuffer;

        private volatile CaptureStatus status = new(CaptureState.Searching);

        public bool IsCapturing => isCapturing;
        public string? DeviceName => lockedAdapter?.Device.Name;
        public CaptureStatus Status => status;
        public event Action<CaptureStatus>? StatusChanged;


        public CaptureDevice(
            ILogger<CaptureDevice> logger,
            TcpStreamBuffer tcpStreamBuffer)
        {
            this.logger = logger;
            this.tcpStreamBuffer = tcpStreamBuffer;
            packetQueue = new BlockingCollection<PooledRawPacket>(100_000);
        }


        public void DiscoverAdapters()
        {
            lock (detectionLock)
            {
                foreach (var dev in CaptureDeviceList.Instance)
                {
                    var kind = ClassifyAdapter(dev);
                    if (kind == AdapterKind.Skip) continue;
                    if (allAdapters.Any(a => a.Device.Name == dev.Name)) continue;

                    allAdapters.Add(new AdapterContext(dev, kind));
                    logger.LogDebug($"[DISCOVER] {kind,-8} adapter: {dev.Name} — {dev.Description}");
                }

                if (allAdapters.Count == 0)
                    logger.LogWarning("[DISCOVER] No eligible adapters found yet; will look again while searching.");
            }
        }

        /// <summary>Adds adapters that appeared since start and opens them if we're searching. Returns how many.</summary>
        private int RefreshAdapters()
        {
            CaptureDeviceList list;
            try
            {
                list = CaptureDeviceList.New();
            }
            catch (Exception ex)
            {
                logger.LogWarning($"[DISCOVER] Could not list adapters: {ex.Message}");
                return 0;
            }

            int added = 0;
            lock (detectionLock)
            {
                foreach (var dev in list)
                {
                    var kind = ClassifyAdapter(dev);
                    if (kind == AdapterKind.Skip) continue;
                    if (allAdapters.Any(a => a.Device.Name == dev.Name)) continue;

                    var ctx = new AdapterContext(dev, kind);
                    allAdapters.Add(ctx);
                    added++;
                    logger.LogInformation($"[DISCOVER] New {kind} adapter: {dev.Name} — {dev.Description}");
                    if (isCapturing && !isLocked) OpenAdapterForDetection(ctx);
                }
            }
            return added;
        }

        private AdapterContext[] AdapterSnapshot()
        {
            lock (detectionLock) return allAdapters.ToArray();
        }

        private static AdapterKind ClassifyAdapter(ICaptureDevice dev)
        {
            var name = dev.Name ?? string.Empty;
            var desc = dev.Description ?? string.Empty;

            if (desc.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("lo", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("\\Device\\NPF_Loopback", StringComparison.OrdinalIgnoreCase))
                return AdapterKind.Loopback;

            if (desc.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase))
                return AdapterKind.Skip;

            return AdapterKind.Physical;
        }


        public void StartCapture()
        {
            if (isCapturing) return;

            ResetAllState();

            processingThread = new Thread(ProcessingLoop)
            {
                Name = "PacketProcessor",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            processingThread.Start();

            isCapturing = true;
            var adapters = AdapterSnapshot();
            foreach (var ctx in adapters)
                OpenAdapterForDetection(ctx);

            statsTimer = new Timer(LogDriverStats, null, 5_000, 5_000);
            adapterRefreshTimer = new Timer(_ => { if (isCapturing && !isLocked) RefreshAdapters(); },
                null, AdapterRefreshMs, AdapterRefreshMs);

            logger.LogInformation(
                $"[CAPTURE] Started on {adapters.Length} adapter(s). " +
                $"Listening for game keepalives on all. Loopback preferred.");
        }

        public void StopCapture()
        {
            if (!isCapturing) return;
            isCapturing = false;

            statsTimer?.Dispose(); statsTimer = null;
            watchdogTimer?.Dispose(); watchdogTimer = null;
            graceWindowTimer?.Dispose(); graceWindowTimer = null;
            adapterRefreshTimer?.Dispose(); adapterRefreshTimer = null;

            CloseAllAdapters();

            cts.Cancel();
            processingThread?.Join(1_000);

            logger.LogInformation("[CAPTURE] Stopped.");
        }

        public void Resync()
        {
            if (!isCapturing) return;
            logger.LogInformation("[CAPTURE] Re-sync requested.");
            RefreshAdapters();
            ResetToDetection();
        }

        public void Dispose()
        {
            StopCapture();
            cts.Dispose();
            packetQueue.Dispose();
        }


        private void OpenAdapterForDetection(AdapterContext ctx)
        {
            try
            {
                // SharpPcap drops the handlers on Close(), so attach on every open - but never twice.
                ctx.Handler ??= (s, e) => OnPacketArrival(ctx, e);
                ctx.Device.OnPacketArrival -= ctx.Handler;
                ctx.Device.OnPacketArrival += ctx.Handler;
                ctx.Device.Open(new DeviceConfiguration
                {
                    Mode = DeviceModes.None,
                    ReadTimeout = ReadTimeoutMs,
                    BufferSize = KernelBufferSize
                });
                ctx.Device.Filter = "tcp";
                ctx.Device.StartCapture();
                ctx.IsOpen = true;

                logger.LogDebug($"[DETECT] Opened {ctx.Kind} adapter: {ctx.Device.Name}");
            }
            catch (Exception ex)
            {
                logger.LogWarning($"[DETECT] Could not open {ctx.Device.Name}: {ex.Message}");
            }
        }

        private void CloseAdapter(AdapterContext ctx)
        {
            if (!ctx.IsOpen) return;
            try { ctx.Device.StopCapture(); } catch { /* ignored */ }
            try { ctx.Device.Close(); } catch { /* ignored */ }
            ctx.IsOpen = false;
        }

        private void CloseAllAdapters()
        {
            foreach (var ctx in AdapterSnapshot())
                CloseAdapter(ctx);
        }

        private void OnPacketArrival(AdapterContext ctx, SharpPcap.PacketCapture e)
        {
            try
            {
                var raw = e.GetPacket();
                var length = raw.Data.Length;
                if (length == 0) return;

                byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
                Array.Copy(raw.Data, 0, buffer, 0, length);

                var pooled = new PooledRawPacket(
                    raw.LinkLayerType, buffer, length,
                    raw.Timeval.Date,
                    ctx);

                if (!packetQueue.TryAdd(pooled))
                {
                    pooled.Dispose();
                    Interlocked.Increment(ref appDroppedPackets);
                }
            }
            catch { /* ignore */ }
        }


        private void ProcessingLoop()
        {
            try
            {
                foreach (var raw in packetQueue.GetConsumingEnumerable(cts.Token))
                {
                    try { ProcessPacket(raw); }
                    catch (Exception ex)
                    { logger.LogError($"Error processing packet: {ex.Message}"); }
                    finally { raw.Dispose(); }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void ProcessPacket(PooledRawPacket raw)
        {
            var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Buffer);
            var tcp = packet.Extract<TcpPacket>();
            if (tcp == null || !tcp.HasPayloadData) return;

            if (!isLocked)
            {
                HandleDetectionPacket(raw.SourceAdapter!, tcp);
                KeepForReplay(raw, tcp);
                return;
            }

            ReplayDetectionBacklog();

            if (raw.SourceAdapter != lockedAdapter) return;
            if (tcp.SourcePort != lockedPort) return;

            HandleLockedSegment(tcp, raw.Timestamp);
        }

        // ── Detection backlog ────────────────────────────────────────────────
        // Detection takes 2-3 s. Right after a zone change (when a re-lock usually happens) that is
        // when the monsters' spawn packets arrive, and without them a boss has no name and Boss Only
        // drops it. So segments from flows that already showed keepalives are kept and replayed once
        // one of them wins. Only touched on the processing thread, apart from Clear on reset.

        private const int MaxBacklogPerFlow = 4000;
        private readonly Dictionary<(AdapterContext Adapter, int Port), List<(LinkLayers Link, byte[] Data, DateTime At)>> detectionBacklog = new();
        private readonly object backlogLock = new();

        private void KeepForReplay(PooledRawPacket raw, TcpPacket tcp)
        {
            var adapter = raw.SourceAdapter!;
            int port = tcp.SourcePort;
            lock (detectionLock)
            {
                if (!detectionHits.TryGetValue(adapter.Device.Name, out var ports) || !ports.ContainsKey(port)) return;
            }

            var copy = new byte[raw.Length];
            Array.Copy(raw.Buffer, copy, raw.Length);
            lock (backlogLock)
            {
                if (!detectionBacklog.TryGetValue((adapter, port), out var list))
                    detectionBacklog[(adapter, port)] = list = [];
                if (list.Count < MaxBacklogPerFlow) list.Add((raw.LinkLayerType, copy, raw.Timestamp));
            }
        }

        private void ReplayDetectionBacklog()
        {
            List<(LinkLayers Link, byte[] Data, DateTime At)>? backlog;
            lock (backlogLock)
            {
                if (detectionBacklog.Count == 0) return;
                var winner = lockedAdapter;
                backlog = winner is null ? null : detectionBacklog.GetValueOrDefault((winner, lockedPort));
                detectionBacklog.Clear();
            }
            if (backlog is null) return;

            logger.LogInformation($"[CAPTURE] Replaying {backlog.Count} segment(s) seen while detecting.");
            foreach (var (link, data, at) in backlog)
            {
                var tcp = Packet.ParsePacket(link, data).Extract<TcpPacket>();
                if (tcp is { HasPayloadData: true }) HandleLockedSegment(tcp, at);
            }
        }

        private void ClearDetectionBacklog()
        {
            lock (backlogLock) detectionBacklog.Clear();
        }

        private void HandleLockedSegment(TcpPacket tcp, DateTime timestamp)
        {
            // Only real keepalives prove the game is still there; any other traffic on the port
            // (a wrong lock on an HTTPS port) must not keep the watchdog happy.
            int beats = KeepAliveSignature.Count(tcp.PayloadData, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (beats > 0) Interlocked.Add(ref watchdogHeartbeatsSinceCheck, beats);

            string streamKey = $"Client:{tcp.DestinationPort}";
            long arrivedAt = new DateTimeOffset(timestamp, TimeSpan.Zero)
                                   .ToUnixTimeMilliseconds();

            uint currentSeq = tcp.SequenceNumber;
            uint payloadLen = (uint)tcp.PayloadData.Length;

            uint virtualLen = payloadLen;
            if (tcp.Synchronize) virtualLen++;
            if (tcp.Finished) virtualLen++;

            uint nextSeq = currentSeq + virtualLen;

            lock (expectedSeqNumbers)
            {
                if (!expectedSeqNumbers.TryGetValue(streamKey, out uint expectedSeq))
                {
                    expectedSeqNumbers[streamKey] = nextSeq;
                    if (tcp.PayloadData.Length < 1) return;
                    tcpStreamBuffer.AddData(streamKey, tcp.PayloadData, arrivedAt);
                    return;
                }

                if (currentSeq < expectedSeq)
                {
                    if (nextSeq <= expectedSeq)
                    {
                        logger.LogTrace(
                            $"[TCP] Duplicate/Old ignored. Seq:{currentSeq} Exp:{expectedSeq}");
                        return;
                    }
                }

                if (currentSeq > expectedSeq)
                {
                    logger.LogWarning(
                        $"[TCP GAP] Expected:{expectedSeq} Got:{currentSeq} " +
                        $"Diff:{currentSeq - expectedSeq}");

                    expectedSeqNumbers[streamKey] = nextSeq;
                    tcpStreamBuffer.AddData(streamKey, tcp.PayloadData, arrivedAt);
                    return;
                }

                // currentSeq == expectedSeq — normal in-order delivery
                expectedSeqNumbers[streamKey] = nextSeq;
                if (tcp.PayloadData.Length < 1) return;
                tcpStreamBuffer.AddData(streamKey, tcp.PayloadData, arrivedAt);
            }
        }



        private void HandleDetectionPacket(AdapterContext ctx, TcpPacket tcp)
        {
            if (tcp.PayloadData.Length < KeepAliveSignature.Length) return;

            int found = KeepAliveSignature.Count(tcp.PayloadData, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (found == 0) return;

            int port = tcp.SourcePort;

            lock (detectionLock)
            {
                if (isLocked) return; // race: locked between queue enqueue and here

                if (!detectionHits.TryGetValue(ctx.Device.Name, out var portMap))
                {
                    portMap = new Dictionary<int, int>();
                    detectionHits[ctx.Device.Name] = portMap;
                }

                portMap.TryGetValue(port, out int hits);
                hits += found;
                portMap[port] = hits;

                logger.LogDebug(
                    $"[DETECT] Keepalive on {ctx.Kind} adapter '{ctx.Device.Name}' " +
                    $"port {port} — hits {hits}/{DetectionThreshold}");

                if (hits >= DetectionThreshold && !graceWindowActive)
                {
                    graceWindowActive = true;
                    logger.LogInformation(
                        $"[DETECT] Threshold reached on {ctx.Kind} '{ctx.Device.Name}' " +
                        $"port {port}. Starting {GraceWindowMs}ms grace window...");

                    graceWindowTimer = new Timer(
                        _ => OnGraceWindowExpired(),
                        null,
                        GraceWindowMs,
                        Timeout.Infinite);
                }
            }
        }


        private void OnGraceWindowExpired()
        {
            CaptureStatus? locked = null;
            lock (detectionLock)
            {
                if (isLocked) return;

                graceWindowTimer?.Dispose();
                graceWindowTimer = null;

                var (winnerAdapter, winnerPort) = PickWinner();

                if (winnerAdapter == null)
                {
                    // Shouldn't happen — grace only starts after threshold, but be safe.
                    logger.LogWarning("[DETECT] Grace window expired but no winner found. Resetting.");
                    graceWindowActive = false;
                    detectionHits.Clear();
                    return;
                }

                CommitLock(winnerAdapter, winnerPort);
                locked = status;
            }
            if (locked is not null) StatusChanged?.Invoke(locked);
        }

        /// <summary>
        /// Picks the best adapter+port from accumulated detection hits.
        /// Priority: Loopback > Physical.
        /// highest hit count wins.
        /// </summary>
        private (AdapterContext? adapter, int port) PickWinner()
        {
            AdapterContext? bestLoopback = null; int bestLoopbackPort = 0; int bestLoopbackHits = 0;
            AdapterContext? bestPhysical = null; int bestPhysicalPort = 0; int bestPhysicalHits = 0;

            foreach (var ctx in allAdapters)
            {
                if (!detectionHits.TryGetValue(ctx.Device.Name, out var portMap)) continue;

                foreach (var kv in portMap)
                {
                    int port = kv.Key;
                    int hits = kv.Value;
                    if (hits < DetectionThreshold) continue;

                    if (ctx.Kind == AdapterKind.Loopback && hits > bestLoopbackHits)
                    {
                        bestLoopback = ctx;
                        bestLoopbackPort = port;
                        bestLoopbackHits = hits;
                    }
                    else if (ctx.Kind == AdapterKind.Physical && hits > bestPhysicalHits)
                    {
                        bestPhysical = ctx;
                        bestPhysicalPort = port;
                        bestPhysicalHits = hits;
                    }
                }
            }

            if (bestLoopback != null)
            {
                logger.LogInformation(
                    $"[DETECT] Winner: LOOPBACK '{bestLoopback.Device.Name}' " +
                    $"port {bestLoopbackPort} ({bestLoopbackHits} hits)." +
                    (bestPhysical != null
                        ? $" Physical '{bestPhysical.Device.Name}' port {bestPhysicalPort} " +
                          $"also had {bestPhysicalHits} hits — loopback preferred."
                        : string.Empty));

                return (bestLoopback, bestLoopbackPort);
            }

            if (bestPhysical != null)
            {
                logger.LogInformation(
                    $"[DETECT] Winner: PHYSICAL '{bestPhysical.Device.Name}' " +
                    $"port {bestPhysicalPort} ({bestPhysicalHits} hits). " +
                    $"No loopback candidate found.");

                return (bestPhysical, bestPhysicalPort);
            }

            return (null, 0);
        }


        // Called under detectionLock; the caller raises StatusChanged once the lock is released.
        private void CommitLock(AdapterContext winner, int port)
        {
            isLocked = true;
            lockedAdapter = winner;
            lockedPort = port;
            graceWindowActive = false;
            status = new CaptureStatus(CaptureState.Locked, port, winner.Kind.ToString());

            string filter = $"tcp src port {port}";
            try
            {
                winner.Device.Filter = filter;
                logger.LogInformation(
                    $"[CAPTURE] !!! LOCKED !!! {winner.Kind} adapter " +
                    $"'{winner.Device.Name}' port {port}. " +
                    $"Kernel filter: \"{filter}\"");
            }
            catch (Exception ex)
            {
                logger.LogError($"[CAPTURE] Failed to apply kernel filter: {ex.Message}");
            }

            foreach (var ctx in allAdapters)
            {
                if (ctx != winner) CloseAdapter(ctx);
            }

            detectionHits.Clear();

            watchdogHeartbeatsSinceCheck = 0;
            watchdogTimer = new Timer(
                CheckWatchdog, null,
                WatchdogTimeoutMs,
                WatchdogTimeoutMs);
        }


        private void CheckWatchdog(object? _)
        {
            if (!isCapturing || !isLocked) return;

            int beats = Interlocked.Exchange(ref watchdogHeartbeatsSinceCheck, 0);
            if (beats > 0)
            {
                logger.LogDebug($"[WATCHDOG] OK — {beats} keepalive(s) in last window.");
                return;
            }

            logger.LogWarning(
                $"[WATCHDOG] No keepalive for {WatchdogTimeoutMs / 1000}s on " +
                $"'{lockedAdapter?.Device.Name}' port {lockedPort}. " +
                $"Resetting to detection mode.");

            ResetToDetection();
        }


        private void ResetToDetection()
        {
            watchdogTimer?.Dispose(); watchdogTimer = null;
            graceWindowTimer?.Dispose(); graceWindowTimer = null;

            lock (detectionLock)
            {
                isLocked = false;
                graceWindowActive = false;
                lockedAdapter = null;
                lockedPort = 0;
                detectionHits.Clear();
                status = new CaptureStatus(CaptureState.Searching);
            }

            lock (expectedSeqNumbers)
            {
                expectedSeqNumbers.Clear();
            }

            ClearDetectionBacklog();
            tcpStreamBuffer.Clear();

            appDroppedPackets = 0;

            foreach (var ctx in AdapterSnapshot())
            {
                if (!ctx.IsOpen)
                    OpenAdapterForDetection(ctx);
                else
                {
                    try { ctx.Device.Filter = "tcp"; }
                    catch { /* ignored */ }
                }
            }

            logger.LogInformation("[CAPTURE] Detection mode re-started on all adapters.");
            StatusChanged?.Invoke(status);
        }

        private void ResetAllState()
        {
            lock (detectionLock)
            {
                isLocked = false;
                graceWindowActive = false;
                lockedAdapter = null;
                lockedPort = 0;
                detectionHits.Clear();
                status = new CaptureStatus(CaptureState.Searching);
            }

            lock (expectedSeqNumbers)
            {
                expectedSeqNumbers.Clear();
            }

            ClearDetectionBacklog();
            appDroppedPackets = 0;
            watchdogHeartbeatsSinceCheck = 0;
        }


        private void LogDriverStats(object? _)
        {
            if (!isCapturing) return;
            foreach (var ctx in AdapterSnapshot())
            {
                if (!ctx.IsOpen) continue;
                try
                {
                    var s = ctx.Device.Statistics;
                    logger.LogDebug(
                        $"[STATS] {ctx.Kind} '{ctx.Device.Name}' — " +
                        $"Recv:{s.ReceivedPackets} DriverDrops:{s.DroppedPackets} " +
                        $"AppDrops:{appDroppedPackets}");
                }
                catch { /* ignored */ }
            }
        }
    }

}
