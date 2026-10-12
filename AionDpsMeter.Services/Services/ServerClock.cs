namespace AionDpsMeter.Services.Services
{
    /// <summary>
    /// Tracks how far the game server's clock is from this PC's, so server timestamps
    /// (e.g. the frenzy deadline) can be turned into local time. PC clocks are often
    /// several seconds off.
    /// </summary>
    public sealed class ServerClock
    {
        private long offsetMs;
        public bool IsSynced { get; private set; }

        public void Update(long serverUnixMs, long receivedAtUnixMs)
        {
            Interlocked.Exchange(ref offsetMs, serverUnixMs - receivedAtUnixMs);
            IsSynced = true;
        }

        public DateTime ToLocalUtc(long serverUnixMs)
            => DateTimeOffset.FromUnixTimeMilliseconds(serverUnixMs - Interlocked.Read(ref offsetMs)).UtcDateTime;
    }
}
