using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using VmView.Models;
using VmView.Services;
using VmView.ViewModels;
using VmView.Views;

namespace VmView;

/// <summary>
/// One window, created once and shown/hidden. Closing it parks the app in the tray with its state
/// intact (the shell suspends what the host could notice); showing it resumes exactly where the user
/// was. Only the tray's Exit ends the process. The tray icon is built in code — TrayIcon is not a
/// StyledElement, so it has no DataContext to bind through — and shows the "live" glyph while a
/// console is streaming.
/// </summary>
public partial class App : Application
{
    /// <summary>Set by Program before the framework starts.</summary>
    public static SingleInstance? Instance { get; set; }
    public static bool StartInTray { get; set; }

    IClassicDesktopStyleApplicationLifetime? _desktop;
    ShellViewModel? _shell;
    ShellWindow? _window;
    TrayIcon? _tray;
    NativeMenuItem? _autostartItem;
    WindowIcon? _idleIcon, _liveIcon;
    bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _shell = new ShellViewModel(Options.Load());
            _shell.PropertyChanged += OnShellChanged;
            _window = new ShellWindow { DataContext = _shell };
            _window.Closing += OnWindowClosing;

            _idleIcon = LoadIcon("vmview.ico");
            _liveIcon = LoadIcon("vmview-live.ico");
            _tray = BuildTray();
            TrayIcon.SetIcons(this, [_tray]);

            desktop.Exit += (_, _) =>
            {
                _tray?.Dispose();
                _shell.Dispose();
            };

            Instance?.Listen(() => Dispatcher.UIThread.Post(ShowWindow));

            if (StartInTray) { _shell.Suspend(); RefreshTray(); }
            else ShowWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    // ----- window ------------------------------------------------------------------------------------------

    void ShowWindow()
    {
        if (_window is null || _shell is null) return;
        _shell.Resume();
        if (_desktop is not null && _desktop.MainWindow is null) _desktop.MainWindow = _window;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        RefreshTray();
    }

    void HideToTray()
    {
        if (_window is null || _shell is null) return;
        _shell.Suspend();
        _window.Hide();
        RefreshTray();
    }

    void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;
        e.Cancel = true;
        HideToTray();
    }

    void Exit()
    {
        _exiting = true;
        _desktop?.Shutdown();
    }

    // ----- tray --------------------------------------------------------------------------------------------

    TrayIcon BuildTray()
    {
        var open = new NativeMenuItem("Open VM Browser");
        open.Click += (_, _) => ShowWindow();

        _autostartItem = new NativeMenuItem("Start with Windows") { ToggleType = NativeMenuItemToggleType.CheckBox };
        _autostartItem.Click += (_, _) => ToggleAutostart();

        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => Exit();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);

        var tray = new TrayIcon { Icon = _idleIcon, ToolTipText = "VM Browser", Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => ShowWindow();
        RefreshAutostartItem();
        return tray;
    }

    void RefreshTray()
    {
        if (_tray is null || _shell is null) return;
        var live = _shell.IsLive;
        _tray.Icon = live ? _liveIcon : _idleIcon;
        _tray.ToolTipText = live && _shell.Console is { } c ? $"VM Browser — {c.Vm.Name} live"
            : !_shell.IsShown ? "VM Browser — idle, no console open"
            : "VM Browser";
    }

    void RefreshAutostartItem()
    {
        if (_autostartItem is null) return;
        _autostartItem.IsChecked = Autostart.IsEnabled();
    }

    void ToggleAutostart()
    {
        try
        {
            if (Autostart.IsEnabled()) Autostart.Disable();
            else Autostart.Enable();
        }
        catch (Exception ex)
        {
            if (_shell is not null) _shell.AppError = $"autostart: {ex.Message}";
        }
        RefreshAutostartItem();
    }

    void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.IsLive) or nameof(ShellViewModel.Console) or nameof(ShellViewModel.IsShown))
            RefreshTray();
    }

    static WindowIcon LoadIcon(string name)
    {
        using var s = AssetLoader.Open(new Uri($"avares://VmView/Assets/{name}"));
        return new WindowIcon(s);
    }
}
