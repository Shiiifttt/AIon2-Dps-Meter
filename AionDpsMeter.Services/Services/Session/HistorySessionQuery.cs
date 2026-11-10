namespace AionDpsMeter.Services.Services.Session
{
    public sealed class HistorySessionQuery
    {
        public DateTime? DateFrom { get; init; }
        public DateTime? DateTo { get; init; }
        public string? BossNameContains { get; init; }
        /// <summary>Only fights where the boss died; fights still in progress are always included.</summary>
        public bool OnlyClears { get; init; }
        public int PageNumber { get; init; } = 1;
        public int PageSize { get; init; } = 100;
    }
}
