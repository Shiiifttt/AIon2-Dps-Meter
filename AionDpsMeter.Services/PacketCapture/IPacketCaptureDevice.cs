namespace AionDpsMeter.Services.PacketCapture;

public enum CaptureState
{
    /// <summary>Listening on every adapter for the game's keepalives.</summary>
    Searching,
    /// <summary>Following the game connection on one adapter and server port.</summary>
    Locked,
}

/// <param name="Port">Game server port while locked, else 0.</param>
/// <param name="Adapter">"Loopback" / "Physical" while locked, else null.</param>
public sealed record CaptureStatus(CaptureState State, int Port = 0, string? Adapter = null);

public interface IPacketCaptureDevice : IDisposable
{
    bool IsCapturing { get; }
    string? DeviceName { get; }
    CaptureStatus Status { get; }

    /// <summary>Locked onto the game, or lost it and went back to searching. Raised on a capture thread.</summary>
    event Action<CaptureStatus>? StatusChanged;

    void StartCapture();
    void StopCapture();
    void DiscoverAdapters();

    /// <summary>Drops the current lock, picks up adapters added since start (VPNs) and searches again.</summary>
    void Resync();
}
