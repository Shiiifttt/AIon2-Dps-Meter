using System.Diagnostics;
using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Entity;
using Microsoft.Extensions.Logging;

namespace AionDpsMeter.Services.Services.Sync
{
    public enum SyncNoticeKind
    {
        /// <summary>Meter is on, game not seen yet.</summary>
        Waiting,
        /// <summary>Following the game; Detail says who you are.</summary>
        Synced,
        /// <summary>Following the game, but who you are isn't known yet.</summary>
        SyncedUnknownUser,
        /// <summary>Game is running but none of its traffic has been seen.</summary>
        NoGameTraffic,
        /// <summary>Was following the game, then its traffic stopped while it's still running.</summary>
        Lost,
    }

    /// <param name="Hold">How long the card stays before fading; null = until the next notice.</param>
    public sealed record SyncNotice(SyncNoticeKind Kind, string Title, string Detail, IReadOnlyList<string> Hints, TimeSpan? Hold);

    /// <summary>
    /// Tells you whether the meter is following the game and who it thinks you are, and what to do
    /// when it isn't. One notice at a time; each replaces the last. Also re-syncs on its own once
    /// when the game is running but no traffic has been seen for a while.
    /// </summary>
    public sealed class MeterSyncMonitor : IDisposable
    {
        private const string GameProcessName = "AION2";
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(3);
        // Locking normally takes 2-3 s once the game talks to its server.
        private static readonly TimeSpan NoTrafficAfter = TimeSpan.FromSeconds(20);
        // A loading screen can pause traffic long enough to drop the lock; it usually comes back quickly.
        private static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan SyncedHold = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan UnknownUserHold = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan WaitingHold = TimeSpan.FromSeconds(6);

        private readonly IPacketCaptureDevice capture;
        private readonly ILogger<MeterSyncMonitor> logger;
        private readonly Lock gate = new();
        private readonly Timer timer;

        private bool locked;
        private bool everLocked;           // since the game was last seen starting
        private DateTime searchingSince = DateTime.UtcNow;
        private bool gameRunning;
        private DateTime gameSeenAt;
        private bool startupNoticeDone;
        private bool troubleShown;         // NoGameTraffic / Lost card is up for the current episode
        private bool autoResyncDone;

        private string? userName;          // null = not recognised
        private bool userNamed;
        private string userServer = "";
        private string? userClass;
        private int userCombatPower;

        public event Action<SyncNotice>? Notice;
        public event Action? SummaryChanged;

        /// <summary>The last notice, for a popup opened after it was raised.</summary>
        public SyncNotice? Current { get; private set; }

        public MeterSyncMonitor(IPacketCaptureDevice capture, EntityTracker entityTracker, ILogger<MeterSyncMonitor> logger)
        {
            this.capture = capture;
            this.logger = logger;
            capture.StatusChanged += OnCaptureStatus;
            entityTracker.UserRecognised += OnUserRecognised;
            locked = capture.Status.State == CaptureState.Locked;
            timer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
        }

        /// <summary>One line for the tray tooltip.</summary>
        public string Summary
        {
            get
            {
                lock (gate)
                {
                    if (locked) return userName is null ? "Synced · who you are not known yet" : $"Synced · {UserLine()}";
                    if (gameRunning) return troubleShown ? "Game running, but its traffic isn't seen" : "Connecting to the game…";
                    return "Waiting for AION2";
                }
            }
        }

        /// <summary>Tray "Re-sync": drop the current lock and search again (also picks up new adapters).</summary>
        public void Resync()
        {
            lock (gate)
            {
                troubleShown = false;
                autoResyncDone = true;
                searchingSince = DateTime.UtcNow;
            }
            Raise(new SyncNotice(SyncNoticeKind.Waiting, "Re-syncing", "Looking for the game connection again…", [], WaitingHold));
            // Reopens every adapter; keep it off the UI thread the tray menu runs on.
            Task.Run(capture.Resync);
        }

        private void OnCaptureStatus(CaptureStatus status)
        {
            SyncNotice? notice = null;
            lock (gate)
            {
                bool nowLocked = status.State == CaptureState.Locked;
                if (nowLocked == locked) return;
                locked = nowLocked;
                if (locked)
                {
                    everLocked = true;
                    troubleShown = false;
                    autoResyncDone = false;
                    notice = SyncedNotice();
                }
                else
                {
                    searchingSince = DateTime.UtcNow;
                }
            }
            if (notice is not null) Raise(notice);
            SummaryChanged?.Invoke();
        }

        // Raised on the packet thread; the login packet repeats on every zone change, so only changes count.
        private void OnUserRecognised(Player player)
        {
            SyncNotice? notice = null;
            lock (gate)
            {
                var cls = player.CharacterClass?.Name;
                bool sameUser = userName == player.Name && userNamed == player.IsIdentified;
                bool classLearned = sameUser && userClass is null && cls is not null;

                userName = player.Name;
                userNamed = player.IsIdentified;
                userServer = player.ServerName ?? "";
                userClass = cls ?? (sameUser ? userClass : null);
                if (player.CombatPower > 0) userCombatPower = player.CombatPower;

                if (sameUser && !classLearned) return;
                logger.LogInformation("[SYNC] You are {User}", UserLine());
                if (locked && !sameUser) notice = SyncedNotice();
            }
            if (notice is not null) Raise(notice);
            SummaryChanged?.Invoke();
        }

        private void Tick()
        {
            SyncNotice? notice = null;
            bool resync = false;
            bool summaryChanged = false;
            try
            {
                bool running = IsGameRunning();
                var now = DateTime.UtcNow;
                lock (gate)
                {
                    if (running != gameRunning)
                    {
                        gameRunning = running;
                        summaryChanged = true;
                        if (running)
                        {
                            gameSeenAt = now;
                            everLocked = locked;
                        }
                        else if (troubleShown)
                        {
                            // Game closed while we were complaining: nothing left to fix.
                            troubleShown = false;
                            notice = new SyncNotice(SyncNoticeKind.Waiting, "Game closed", "Waiting for AION2 to start", [], WaitingHold);
                        }
                    }

                    if (!startupNoticeDone)
                    {
                        startupNoticeDone = true;
                        if (!locked)
                            notice = new SyncNotice(SyncNoticeKind.Waiting, "Meter running",
                                gameRunning ? "Connecting to the game…" : "Waiting for AION2 to start", [], WaitingHold);
                    }

                    if (!locked && gameRunning && !troubleShown)
                    {
                        var since = searchingSince > gameSeenAt ? searchingSince : gameSeenAt;
                        var after = everLocked ? LostAfter : NoTrafficAfter;
                        if (now - since > after)
                        {
                            troubleShown = true;
                            summaryChanged = true;
                            notice = everLocked ? LostNotice() : NoTrafficNotice();
                            if (!autoResyncDone)
                            {
                                autoResyncDone = true;
                                resync = true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("[SYNC] Status check failed: {Message}", ex.Message);
            }

            if (notice is not null) Raise(notice);
            if (summaryChanged) SummaryChanged?.Invoke();
            if (resync)
            {
                logger.LogWarning("[SYNC] Game is running but no game traffic is seen; re-syncing once on its own.");
                capture.Resync();
            }
        }

        private static bool IsGameRunning()
        {
            var processes = Process.GetProcessesByName(GameProcessName);
            foreach (var p in processes) p.Dispose();
            return processes.Length > 0;
        }

        // ── Notices (called under gate) ───────────────────────────────────────

        private SyncNotice SyncedNotice()
        {
            if (userName is null)
                return new SyncNotice(SyncNoticeKind.SyncedUnknownUser, "Meter synced", "Who you are isn't known yet",
                    ["Use a few skills in combat, or change zone / teleport, and the meter will pick you out"],
                    UnknownUserHold);

            var hints = userNamed
                ? (IReadOnlyList<string>)[]
                : ["Your name comes with the next zone change or teleport"];
            return new SyncNotice(SyncNoticeKind.Synced, "Meter synced", $"You: {UserLine()}", hints, SyncedHold);
        }

        private static SyncNotice NoTrafficNotice() => new(SyncNoticeKind.NoGameTraffic,
            "Not synced", "AION2 is running, but the meter can't see its traffic",
            [
                "Tray menu → Re-sync with game",
                "Using a VPN? Connect it, then Re-sync",
                "Still nothing: restart the meter (Npcap must be installed)",
            ],
            Hold: null);

        private static SyncNotice LostNotice() => new(SyncNoticeKind.Lost,
            "Sync lost", "The game's traffic stopped - looking for it again",
            [
                "Usually back by itself after a loading screen",
                "If not: tray menu → Re-sync with game",
            ],
            Hold: null);

        private string UserLine()
        {
            var name = userNamed ? userName : "name not known yet";
            var parts = new List<string> { name! };
            if (userClass is not null) parts.Add(userClass);
            if (userCombatPower > 0) parts.Add($"CP {userCombatPower:N0}");
            if (!string.IsNullOrEmpty(userServer)) parts.Add(userServer);
            return string.Join(" · ", parts);
        }

        private void Raise(SyncNotice notice)
        {
            Current = notice;
            Notice?.Invoke(notice);
        }

        public void Dispose()
        {
            timer.Dispose();
        }
    }
}
