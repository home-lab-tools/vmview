using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using VmView.Rendering;

namespace VmView.Services;

/// <summary>
/// The sidebar preview: a worker thread asks Hyper-V for a scaled copy of the VM's monitor through
/// Msvm_VirtualSystemManagementService.GetVirtualSystemThumbnailImage at a low rate and pushes it into a
/// <see cref="FrameBuffer"/>. Read-only host API; no session, nothing flows toward the VM.
/// </summary>
public sealed class ThumbnailSource : IDisposable
{
    readonly string _host;
    readonly string _vmId;
    readonly object _gate = new();
    Thread? _thread;
    CancellationTokenSource? _cts;
    int _width, _height;
    double _fps;

    public ThumbnailSource(string host, string vmId) => (_host, _vmId) = (host, vmId);

    public FrameBuffer Frame { get; } = new();

    /// <summary>Change size or rate; restarts the worker only when something actually changed.</summary>
    public void Configure(int width, int height, double fps)
    {
        width = Math.Max(16, width & ~1);
        height = Math.Max(16, height & ~1);
        fps = Math.Clamp(fps, 0.2, 10);

        lock (_gate)
        {
            if (_thread is not null && width == _width && height == _height && Math.Abs(fps - _fps) < 0.01) return;
            StopLocked();
            (_width, _height, _fps) = (width, height, fps);
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _thread = new Thread(() => Loop(width, height, fps, ct)) { IsBackground = true, Name = $"thumb {_vmId[..8]}" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        lock (_gate) StopLocked();
        Frame.Clear();
    }

    void StopLocked()
    {
        _cts?.Cancel();
        _cts = null;
        _thread = null;
    }

    void Loop(int width, int height, double fps, CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(1 / fps);
        var clock = Stopwatch.StartNew();
        ManagementObject? service = null;
        string? settingsPath = null;

        while (!ct.IsCancellationRequested)
        {
            var started = clock.Elapsed;
            var ok = false;
            try
            {
                var scope = new ManagementScope(HyperVInventory.ScopePath(_host));
                service ??= HyperVInventory.ManagementService(scope);
                settingsPath ??= SettingsPath(scope);

                using var inParams = service.GetMethodParameters("GetVirtualSystemThumbnailImage");
                inParams["WidthPixels"] = (ushort)width;
                inParams["HeightPixels"] = (ushort)height;
                inParams["TargetSystem"] = settingsPath;
                using var outParams = service.InvokeMethod("GetVirtualSystemThumbnailImage", inParams, null);

                // Hyper-V prefixes a small header; the RGB565 rows sit at the tail of the buffer.
                if (Convert.ToUInt32(outParams["ReturnValue"]) == 0 && outParams["ImageData"] is byte[] bytes && bytes.Length >= width * height * 2)
                {
                    Frame.WriteRgb565(bytes, bytes.Length - width * height * 2, width, height);
                    ok = true;
                }
            }
            catch (Exception ex) when (ex is ManagementException or COMException)
            {
                service?.Dispose();
                service = null;
                settingsPath = null;
            }

            var wait = ok ? period - (clock.Elapsed - started) : TimeSpan.FromSeconds(2);
            if (wait > TimeSpan.Zero && ct.WaitHandle.WaitOne(wait)) break;
        }
        service?.Dispose();
    }

    /// <summary>The VM's realized Msvm_VirtualSystemSettingData — what the thumbnail method calls TargetSystem.</summary>
    string SettingsPath(ManagementScope scope)
    {
        using var search = new ManagementObjectSearcher(scope, new ObjectQuery(
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name='{_vmId}'"));
        using var vm = search.Get().Cast<ManagementObject>().FirstOrDefault()
            ?? throw new ManagementException($"VM {_vmId} not found on {_host}");
        using var settings = vm.GetRelated("Msvm_VirtualSystemSettingData", "Msvm_SettingsDefineState",
                null, null, "SettingData", "ManagedElement", false, null)
            .Cast<ManagementObject>().FirstOrDefault()
            ?? throw new ManagementException($"VM {_vmId} has no realized settings");
        return settings.Path.Path;
    }

    public void Dispose() => Stop();
}
