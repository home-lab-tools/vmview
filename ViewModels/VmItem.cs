using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VmView.Models;
using VmView.Rendering;
using VmView.Services;

namespace VmView.ViewModels;

/// <summary>One VM in the catalog: its latest summary plus a small live preview of its monitor.</summary>
public sealed partial class VmItem : ObservableObject, IDisposable
{
    static readonly IBrush Green = Brush.Parse("#22C55E");
    static readonly IBrush Amber = Brush.Parse("#F59E0B");
    static readonly IBrush Blue = Brush.Parse("#3B82F6");
    static readonly IBrush Violet = Brush.Parse("#A78BFA");
    static readonly IBrush Slate = Brush.Parse("#64748B");
    static readonly IBrush Red = Brush.Parse("#EF4444");

    readonly Options _opt;
    readonly ThumbnailSource _thumb;
    bool _previewsEnabled = true;
    int _previewWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Key), nameof(StateText), nameof(StateBrush), nameof(HeartbeatText),
        nameof(HeartbeatBrush), nameof(LoadText), nameof(UpTimeText), nameof(GuestText), nameof(ResolutionText),
        nameof(HasConsole), nameof(HostText), nameof(ShowState))]
    VmSummary _summary;

    public VmItem(Options opt, VmSummary summary)
    {
        _opt = opt;
        _summary = summary;
        _thumb = new ThumbnailSource(summary.Host, summary.Id);
        Apply(summary);
    }

    public FrameBuffer Preview => _thumb.Frame;

    public string Key => Summary.Key;
    public string Name => Summary.Name;
    public string HostText => Summary.Host is "." ? Environment.MachineName : Summary.Host;
    public bool HasConsole => Summary.HasConsole;

    /// <summary>The state pill is for the states that are not obvious — a live preview already says "Running".</summary>
    public bool ShowState => Summary.State != VmState.Running;

    public string StateText => Summary.State switch
    {
        VmState.Running => "Running",
        VmState.Off => "Off",
        VmState.Saved => "Saved",
        VmState.Paused => "Paused",
        VmState.Starting => "Starting",
        VmState.Stopping => "Stopping",
        VmState.Saving => "Saving",
        VmState.Pausing => "Pausing",
        VmState.Resuming => "Resuming",
        VmState.Reset => "Resetting",
        _ => $"State {(int)Summary.State}",
    };

    public IBrush StateBrush => Summary.State switch
    {
        VmState.Running => Green,
        VmState.Paused or VmState.Pausing => Amber,
        VmState.Saved => Blue,
        VmState.Off => Slate,
        _ => Violet,
    };

    public string HeartbeatText => Summary.Heartbeat switch
    {
        Heartbeat.Ok => "heartbeat ok",
        Heartbeat.Degraded => "heartbeat degraded",
        Heartbeat.Error => "heartbeat error",
        Heartbeat.LostCommunication => "heartbeat lost",
        Heartbeat.Disabled => "no heartbeat",
        _ => "no contact",
    };

    public IBrush HeartbeatBrush => Summary.Heartbeat switch
    {
        Heartbeat.Ok => Green,
        Heartbeat.Degraded => Amber,
        Heartbeat.Error or Heartbeat.LostCommunication => Red,
        _ => Slate,
    };

    public string LoadText => Summary.HasConsole
        ? $"{Summary.ProcessorLoad} % · {Summary.ProcessorCount} vCPU · {Summary.MemoryMb:N0} MB"
        : $"{Summary.ProcessorCount} vCPU";

    public string GuestText => string.IsNullOrWhiteSpace(Summary.GuestOs) ? "guest OS unknown" : Summary.GuestOs;
    public string ResolutionText => Summary.ScreenWidth > 0 ? $"{Summary.ScreenWidth}×{Summary.ScreenHeight}" : "no monitor";

    public string UpTimeText
    {
        get
        {
            if (!Summary.HasConsole) return "";
            var t = Summary.UpTime;
            return "up " + (t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t:hh\\:mm}" : t.ToString(@"hh\:mm\:ss"));
        }
    }

    /// <summary>Requested preview width in pixels (the tile's size); 0 = the configured TileWidth. Never above the VM's own resolution.</summary>
    public int PreviewWidth
    {
        get => _previewWidth;
        set { value = Math.Max(0, value); if (_previewWidth != value) { _previewWidth = value; Apply(Summary); } }
    }

    /// <summary>Whether the preview worker may run at all (the list page is on screen).</summary>
    public bool PreviewsEnabled
    {
        get => _previewsEnabled;
        set { if (_previewsEnabled != value) { _previewsEnabled = value; Apply(Summary); } }
    }

    /// <summary>Take a fresh summary and keep the preview matched to the VM's state and monitor size.</summary>
    public void Apply(VmSummary summary)
    {
        Summary = summary;
        if (summary.HasConsole && _previewsEnabled)
        {
            var box = _previewWidth > 0 ? _previewWidth : _opt.TileWidth;
            var (w, h) = summary.FitScreen(box, box);
            _thumb.Configure(w, h, _opt.TileFps);
        }
        else
        {
            _thumb.Stop();
        }
    }

    public void Dispose() => _thumb.Dispose();
}
