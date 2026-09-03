using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using VmView.Controls;
using VmView.ViewModels;

namespace VmView.Views;

public partial class ShellWindow : Window
{
    readonly SlideTransition _slide = new();
    ShellViewModel? _shell;
    WindowState _before = WindowState.Normal;
    bool _placed;

    public ShellWindow()
    {
        InitializeComponent();
        Host.PageTransition = _slide;
        DataContextChanged += (_, _) =>
        {
            if (_shell is not null) _shell.PropertyChanged -= OnShellChanged;
            _shell = DataContext as ShellViewModel;
            if (_shell is not null) _shell.PropertyChanged += OnShellChanged;
        };
    }

    /// <summary>Under 840 DIPs the pages drop their secondary labels (styles keyed on the "narrow" class).</summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Classes.Set("narrow", e.NewSize.Width < 840);
    }

    /// <summary>
    /// First open: no larger than 88 % of the monitor's working area, centred. Later opens (back from
    /// the tray) keep whatever size and place the user left it at.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_placed) return;
        _placed = true;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var area = screen.WorkingArea;
        var s = screen.Scaling;
        Width = Math.Min(Width, Math.Floor(area.Width / s * 0.88));
        Height = Math.Min(Height, Math.Floor(area.Height / s * 0.88));
        Position = new PixelPoint(
            area.X + (area.Width - (int)(Width * s)) / 2,
            area.Y + (area.Height - (int)(Height * s)) / 2);
    }

    void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_shell is null) return;
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.Forward):       // raised before Page changes, so the transition sees it
                _slide.Forward = _shell.Forward;
                break;
            case nameof(ShellViewModel.IsFullscreen):
                if (_shell.IsFullscreen)
                {
                    if (WindowState != WindowState.FullScreen) _before = WindowState;
                    WindowState = WindowState.FullScreen;
                }
                else if (WindowState == WindowState.FullScreen)
                {
                    WindowState = _before == WindowState.FullScreen ? WindowState.Normal : _before;
                }
                break;
        }
    }
}
