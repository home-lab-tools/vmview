using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VmView.Models;
using VmView.Services;

namespace VmView.ViewModels;

/// <summary>
/// The one window's state: a two-level navigation stack — the list of running VMs at the root, one
/// VM's console pushed on top — and the tray park/unpark that keeps that state intact. Hiding into the
/// tray suspends everything the host could notice (session, polling, previews); showing again resumes
/// exactly where the user was.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    readonly Options _opt;
    bool _shown = true;

    public ShellViewModel(Options opt)
    {
        _opt = opt;
        Catalog = new VmCatalog(opt);
        Catalog.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(VmCatalog.Error)) OnPropertyChanged(nameof(Error)); };
        var hosts = string.Join(", ", opt.Hosts.Select(h => h is "." ? Environment.MachineName : h));
        List = new ListPageViewModel(Catalog, hosts, Open);
        _page = List;
        Catalog.Polling = true;
        ApplyActivity();
    }

    public VmCatalog Catalog { get; }
    public ListPageViewModel List { get; }
    public string NativeVersion => ConsoleClient.NativeVersion;

    /// <summary>The page on screen: <see cref="List"/> or a <see cref="ConsolePageViewModel"/>.</summary>
    [ObservableProperty] object _page;

    /// <summary>The pushed console page, while there is one.</summary>
    [ObservableProperty] ConsolePageViewModel? _console;

    /// <summary>Direction of the last navigation, read by the page transition (true = deeper).</summary>
    [ObservableProperty] bool _forward = true;

    /// <summary>Errors from the catalog or the app (autostart); the page shows them in its status line.</summary>
    [ObservableProperty] string? _appError;

    public string? Error => AppError ?? Catalog.Error;
    partial void OnAppErrorChanged(string? value) => OnPropertyChanged(nameof(Error));

    public bool IsFullscreen => Console?.IsFullscreen == true;
    public bool IsLive => Console?.IsLive == true;
    public string Title => Console is { } c ? $"{c.Vm.Name} — VM Browser" : "VM Browser";
    public bool IsShown => _shown;

    // ----- navigation ----------------------------------------------------------------------------------------

    /// <summary>Push the console page for a VM (replacing one already open for another VM).</summary>
    public void Open(VmItem vm)
    {
        if (Console is { } current && ReferenceEquals(current.Vm, vm)) return;
        DropConsole();
        Console = new ConsolePageViewModel(vm, Back);
        Console.PropertyChanged += OnConsoleChanged;
        Forward = true;
        Page = Console;
        Changed();
        ApplyActivity();
    }

    [RelayCommand]
    public void Back()
    {
        if (Console is null) return;
        Forward = false;
        Page = List;
        DropConsole();
        Changed();
        ApplyActivity();
    }

    /// <summary>Esc: leave fullscreen first, otherwise go up a level.</summary>
    [RelayCommand]
    void Escape()
    {
        if (Console is { IsFullscreen: true } c) c.IsFullscreen = false;
        else Back();
    }

    [RelayCommand] void ToggleFullscreen() { if (Console is { } c) c.IsFullscreen = !c.IsFullscreen; }
    [RelayCommand] void OpenSelected() { if (Console is null) List.OpenFirstCommand.Execute(null); }

    void DropConsole()
    {
        if (Console is null) return;
        Console.PropertyChanged -= OnConsoleChanged;
        Console.Dispose();
        Console = null;
    }

    void OnConsoleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConsolePageViewModel.IsFullscreen)) OnPropertyChanged(nameof(IsFullscreen));
        if (e.PropertyName is nameof(ConsolePageViewModel.IsLive)) OnPropertyChanged(nameof(IsLive));
    }

    void Changed()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsFullscreen));
        OnPropertyChanged(nameof(IsLive));
    }

    // ----- tray -----------------------------------------------------------------------------------------------

    /// <summary>Window hidden: no session, no polling, no previews. The page stack stays as it is.</summary>
    public void Suspend()
    {
        if (!_shown) return;
        _shown = false;
        Console?.Suspend();
        ApplyActivity();
        OnPropertyChanged(nameof(IsShown));
    }

    /// <summary>Window shown again: polling and, when a console page is up, its session come back.</summary>
    public void Resume()
    {
        if (_shown) return;
        _shown = true;
        ApplyActivity();
        Console?.Resume();
        OnPropertyChanged(nameof(IsShown));
    }

    void ApplyActivity()
    {
        Catalog.Polling = _shown;
        Catalog.PreviewsEnabled = _shown && Console is null;
    }

    public void Dispose()
    {
        DropConsole();
        List.Dispose();
        Catalog.Dispose();
    }
}
