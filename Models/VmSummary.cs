namespace VmView.Models;

/// <summary>Msvm_ComputerSystem.EnabledState as Hyper-V reports it.</summary>
public enum VmState
{
    Unknown = 0,
    Running = 2,
    Off = 3,
    Stopping = 4,
    Saved = 6,
    Paused = 9,
    Starting = 10,
    Reset = 11,
    Saving = 32773,
    Pausing = 32776,
    Resuming = 32777,
}

/// <summary>Msvm_SummaryInformation.Heartbeat — the integration-service pulse.</summary>
public enum Heartbeat
{
    NoContact = 0,
    Ok = 2,
    Degraded = 3,
    Error = 6,
    LostCommunication = 12,
    Disabled = 13,
}

/// <summary>One VM as the browser shows it: identity, state, load, and the size of its virtual monitor.</summary>
public sealed record VmSummary(
    string Host,
    string Id,
    string Name,
    VmState State,
    int ProcessorCount,
    int ProcessorLoad,
    long MemoryMb,
    TimeSpan UpTime,
    Heartbeat Heartbeat,
    string? GuestOs,
    int ScreenWidth,
    int ScreenHeight)
{
    public string Key => $"{Host}/{Id}";

    /// <summary>The console has a picture only while the VM is up (paused still shows the last frame).</summary>
    public bool HasConsole => State is VmState.Running or VmState.Paused or VmState.Pausing or VmState.Resuming;

    public int SafeWidth => ScreenWidth > 0 ? ScreenWidth : 1024;
    public int SafeHeight => ScreenHeight > 0 ? ScreenHeight : 768;

    /// <summary>Aspect-preserving size inside a box, never above native, snapped to even pixels.</summary>
    public (int W, int H) FitScreen(int boxW, int boxH)
    {
        var scale = Math.Min(Math.Min((double)boxW / SafeWidth, (double)boxH / SafeHeight), 1.0);
        return (Even((int)Math.Round(SafeWidth * scale)), Even((int)Math.Round(SafeHeight * scale)));
    }

    static int Even(int v) => Math.Max(16, v - (v & 1));
}
