using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VmView.Rendering;

namespace VmView.Controls;

public enum ScreenZoom
{
    /// <summary>Largest size that fits the pane, aspect kept, centred.</summary>
    Fit,
    /// <summary>One remote pixel per physical pixel.</summary>
    Native,
    /// <summary>Two physical pixels per remote pixel, nearest-neighbour.</summary>
    Double,
}

/// <summary>Where the screen sends input when its input is switched on.</summary>
public interface IConsoleInput
{
    void Mouse(ushort flags, int x, int y);
    void Key(bool down, byte code, bool extended);
}

/// <summary>
/// Paints a <see cref="FrameBuffer"/>. Fit sizes itself to the space it is given; the other zooms size
/// themselves to the picture and let a ScrollViewer pan. Not hit-testable and not focusable — a picture —
/// until <see cref="InputEnabled"/> turns it into a console: then pointer and keys go to <see cref="Input"/>.
/// </summary>
public sealed class ScreenView : Control
{
    // RDP pointer flags
    const ushort Move = 0x0800, Down = 0x8000, Button1 = 0x1000, Button2 = 0x2000, Button3 = 0x4000, Wheel = 0x0200, WheelNegative = 0x0100;

    public static readonly StyledProperty<FrameBuffer?> SourceProperty =
        AvaloniaProperty.Register<ScreenView, FrameBuffer?>(nameof(Source));

    public static readonly StyledProperty<ScreenZoom> ZoomProperty =
        AvaloniaProperty.Register<ScreenView, ScreenZoom>(nameof(Zoom));

    public static readonly StyledProperty<IConsoleInput?> InputProperty =
        AvaloniaProperty.Register<ScreenView, IConsoleInput?>(nameof(Input));

    public static readonly StyledProperty<bool> InputEnabledProperty =
        AvaloniaProperty.Register<ScreenView, bool>(nameof(InputEnabled));

    static ScreenView()
    {
        AffectsMeasure<ScreenView>(SourceProperty, ZoomProperty);
        AffectsRender<ScreenView>(SourceProperty, ZoomProperty);
    }

    public ScreenView()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public FrameBuffer? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public ScreenZoom Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public IConsoleInput? Input { get => GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public bool InputEnabled { get => GetValue(InputEnabledProperty); set => SetValue(InputEnabledProperty, value); }

    int _lastW, _lastH;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            if (change.OldValue is FrameBuffer old) old.Presented -= OnPresented;
            if (change.NewValue is FrameBuffer now) now.Presented += OnPresented;
            (_lastW, _lastH) = (0, 0);
        }
        else if (change.Property == InputEnabledProperty)
        {
            var on = change.GetNewValue<bool>();
            IsHitTestVisible = on;
            Focusable = on;
            Cursor = on ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
            if (on) Focus();
        }
    }

    void OnPresented()
    {
        var (w, h) = (Source?.Width ?? 0, Source?.Height ?? 0);
        if (w != _lastW || h != _lastH)
        {
            (_lastW, _lastH) = (w, h);
            InvalidateMeasure();
        }
        InvalidateVisual();
    }

    double Scaling => VisualRoot?.RenderScaling ?? 1.0;

    // ----- layout / paint ----------------------------------------------------------------------------------

    protected override Size MeasureOverride(Size available)
    {
        var bmp = Source?.Bitmap;
        if (bmp is null) return default;
        var (w, h) = ((double)bmp.PixelSize.Width, (double)bmp.PixelSize.Height);

        switch (Zoom)
        {
            case ScreenZoom.Native: return new Size(w / Scaling, h / Scaling);
            case ScreenZoom.Double: return new Size(2 * w / Scaling, 2 * h / Scaling);
            default:
                var aw = double.IsInfinity(available.Width) ? w : available.Width;
                var ah = double.IsInfinity(available.Height) ? h : available.Height;
                var s = Math.Min(aw / w, ah / h);
                return new Size(Math.Floor(w * s), Math.Floor(h * s));
        }
    }

    public override void Render(DrawingContext context)
    {
        var bmp = Source?.Bitmap;
        if (bmp is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        // Sharp pixels once the picture is enlarged, smooth resampling when it is shrunk.
        var physicalScale = Bounds.Width * Scaling / bmp.PixelSize.Width;
        RenderOptions.SetBitmapInterpolationMode(this, physicalScale >= 1.99 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);

        context.DrawImage(bmp, new Rect(bmp.Size), new Rect(Bounds.Size));
    }

    // ----- input (only reachable while InputEnabled) ---------------------------------------------------------

    bool Remote(Point p, out int x, out int y)
    {
        x = y = 0;
        var bmp = Source?.Bitmap;
        if (bmp is null || Bounds.Width <= 0 || Bounds.Height <= 0) return false;
        x = Math.Clamp((int)(p.X / Bounds.Width * bmp.PixelSize.Width), 0, bmp.PixelSize.Width - 1);
        y = Math.Clamp((int)(p.Y / Bounds.Height * bmp.PixelSize.Height), 0, bmp.PixelSize.Height - 1);
        return true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (Input is { } input && InputEnabled && Remote(e.GetPosition(this), out var x, out var y))
        {
            input.Mouse(Move, x, y);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (Input is not { } input || !InputEnabled || !Remote(e.GetPosition(this), out var x, out var y)) return;
        Focus();
        var button = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => Button1,
            PointerUpdateKind.RightButtonPressed => Button2,
            PointerUpdateKind.MiddleButtonPressed => Button3,
            _ => 0,
        };
        if (button != 0) input.Mouse((ushort)(Down | button), x, y);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (Input is not { } input || !InputEnabled || !Remote(e.GetPosition(this), out var x, out var y)) return;
        var button = e.InitialPressMouseButton switch
        {
            MouseButton.Left => Button1,
            MouseButton.Right => Button2,
            MouseButton.Middle => Button3,
            _ => 0,
        };
        if (button != 0) input.Mouse((ushort)button, x, y);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (Input is not { } input || !InputEnabled || !Remote(e.GetPosition(this), out var x, out var y)) return;
        var notches = (int)Math.Round(e.Delta.Y);
        for (var i = 0; i < Math.Abs(notches); i++)
            input.Mouse(notches > 0 ? (ushort)(Wheel | 0x78) : (ushort)(Wheel | WheelNegative | 0x88), x, y);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e) => SendKey(e, true);
    protected override void OnKeyUp(KeyEventArgs e) => SendKey(e, false);

    void SendKey(KeyEventArgs e, bool down)
    {
        if (Input is not { } input || !InputEnabled) return;
        if (e.Key == Avalonia.Input.Key.F11) return;              // stays a local hotkey: the way out of fullscreen
        var (code, extended) = Scancodes.From(e.PhysicalKey);
        if (code == 0) return;
        input.Key(down, code, extended);
        e.Handled = true;
    }
}
