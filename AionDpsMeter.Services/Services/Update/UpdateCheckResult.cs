namespace AionDpsMeter.Services.Services.Update
{
    public enum UpdateCheckStatus
    {
        Disabled,
        UpToDate,
        Available,
        Failed
    }

    public sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message, ReleaseInfo? Release = null);
}
