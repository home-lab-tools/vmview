using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VmView.Controls;
using VmView.Services;

namespace VmView.ViewModels;

public sealed record ZoomOption(string Label, ScreenZoom Zoom);

/// <summary>
/// The console page, one level down from the list: one VM's session, zoom, fullscreen and the input
/// gate. It follows the VM through the catalog's <see cref="VmItem"/> (reconnects when it comes back,
/// drops the picture when it goes off) and retries on its own timer. <see cref="Suspend"/> parks it
/// for the tray — session closed, everything else kept — and <see cref="Resume"/> reconnects.
/// </summary>
public sealed partial class ConsolePageViewModel : ObservableObject, IDisposable
{
    readonly DispatcherTimer _retry;
    readonly Action _back;
    DateTime _lastConnect = DateTime.MinValue;
    bool _consoleRefused;
    bool _suspended;

    public ConsolePageViewModel(VmItem vm, Action back)
    {
        Vm = vm;
        _back = back;
        vm.PropertyChanged += OnVmChanged;
        _zoomOption = ZoomOptions[0];

        Console.PropertyChanged += OnConsoleChanged;
        Console.Frame.Presented += () => { OnPropertyChanged(nameof(Fps)); OnPropertyChanged(nameof(LiveText)); };

        _retry = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => EnsureConsole());
        _retry.Start();
        EnsureConsole();
    }

    public VmItem Vm { get; }
    public ConsoleClient Console { get; } = new();

    public IReadOnlyList<ZoomOption> ZoomOptions { get; } =
    [
        new("Fit", ScreenZoom.Fit), new("1:1", ScreenZoom.Native), new("2×", ScreenZoom.Double),
    ];

    [ObservableProperty] ZoomOption _zoomOption;
    [ObservableProperty] bool _isFullscreen;

    /// <summary>Keys and mouse go to the VM while on. Off by default; shuts whenever the session drops.</summary>
    [ObservableProperty] bool _inputEnabled;

    public ScreenZoom Zoom => ZoomOption.Zoom;
    public bool IsLive => Console.State == ConsoleState.Connected;
    public bool IsConnecting => Console.State == ConsoleState.Connecting;
    public bool ShowPlaceholder => !IsLive;
    public bool CanReconnect => Console.State is ConsoleState.Failed or ConsoleState.Disconnected && Vm.HasConsole;
    public double Fps => Console.Frame.Fps;
    public string LiveText => IsLive ? $"{Console.Width}×{Console.Height} · {Fps:0} fps" : "";
    public string HudHint => InputEnabled ? "Input on · F11 to leave fullscreen" : "Esc to leave fullscreen";

    /// <summary>What the stage says when there is no picture.</summary>
    public string PlaceholderText =>
        !Vm.HasConsole ? $"{Vm.StateText} — no picture while the VM is {Vm.StateText.ToLowerInvariant()}"
        : Console.State == ConsoleState.Connecting ? "Connecting to the console…"
        : string.IsNullOrEmpty(Console.Message) ? "Waiting for the console…"
        : Console.Message;

    partial void OnZoomOptionChanged(ZoomOption value) => OnPropertyChanged(nameof(Zoom));
    partial void OnInputEnabledChanged(bool value) { Console.InputEnabled = value; OnPropertyChanged(nameof(HudHint)); }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VmItem.Summary))
        {
            OnPropertyChanged(nameof(PlaceholderText));
            OnPropertyChanged(nameof(CanReconnect));
            EnsureConsole();
        }
    }

    void OnConsoleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConsoleClient.State) or nameof(ConsoleClient.Message))
        {
            if (Console.LastError == ConsoleClient.LogonFailure) _consoleRefused = true;
            if (Console.State != ConsoleState.Connected) InputEnabled = false;
            OnPropertyChanged(nameof(IsLive));
            OnPropertyChanged(nameof(IsConnecting));
            OnPropertyChanged(nameof(ShowPlaceholder));
            OnPropertyChanged(nameof(PlaceholderText));
            OnPropertyChanged(nameof(CanReconnect));
            OnPropertyChanged(nameof(LiveText));
        }
        if (e.PropertyName is nameof(ConsoleClient.Width) or nameof(ConsoleClient.Height)) OnPropertyChanged(nameof(LiveText));
    }

    /// <summary>Open the console when the VM has one and we are not on it; close it when the VM lost one.</summary>
    void EnsureConsole()
    {
        if (_suspended) return;
        if (!Vm.HasConsole)
        {
            if (Console.IsOpen) Console.Close();
            return;
        }

        if (Console.State is ConsoleState.Connected or ConsoleState.Connecting) return;
        if (_consoleRefused) return;                                   // no retries against a refused logon
        if (DateTime.UtcNow - _lastConnect < TimeSpan.FromSeconds(3)) return;
        _lastConnect = DateTime.UtcNow;
        Console.Open(Vm.Summary.Host, Vm.Summary.Id);
    }

    /// <summary>Tray: drop the session, keep the page, zoom and everything else as they are.</summary>
    public void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        _retry.Stop();
        IsFullscreen = false;
        InputEnabled = false;
        Console.Close();
    }

    /// <summary>Back from the tray: reconnect at once.</summary>
    public void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        _lastConnect = DateTime.MinValue;
        _retry.Start();
        EnsureConsole();
    }

    [RelayCommand] void Back() => _back();
    [RelayCommand] void ToggleFullscreen() => IsFullscreen = !IsFullscreen;
    [RelayCommand] void ExitFullscreen() => IsFullscreen = false;
    [RelayCommand] void SetZoom(ZoomOption option) => ZoomOption = option;

    [RelayCommand]
    void Reconnect()
    {
        _consoleRefused = false;
        _lastConnect = DateTime.MinValue;
        EnsureConsole();
    }

    public void Dispose()
    {
        _retry.Stop();
        Vm.PropertyChanged -= OnVmChanged;
        Console.Dispose();
    }
}
