using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace VmView.Rendering;

/// <summary>
/// A picture that any thread may paint into and the UI thread presents. Producers write BGRA rectangles into
/// a shadow buffer; the UI thread copies the dirty union into a <see cref="WriteableBitmap"/> and raises
/// <see cref="Presented"/>. Purely a sink: it has no notion of where the pixels came from.
/// </summary>
public sealed class FrameBuffer
{
    readonly object _gate = new();
    byte[] _shadow = [];
    int _w, _h;
    PixelRect _dirty;
    bool _pending;
    readonly Queue<long> _presentTimes = new();

    /// <summary>UI thread only.</summary>
    public WriteableBitmap? Bitmap { get; private set; }
    public int Width => Bitmap?.PixelSize.Width ?? 0;
    public int Height => Bitmap?.PixelSize.Height ?? 0;
    public double Fps { get; private set; }

    /// <summary>Raised on the UI thread after each presentation, or after <see cref="Clear"/>.</summary>
    public event Action? Presented;

    /// <summary>Copy a BGRA rectangle out of a native framebuffer (any thread).</summary>
    public void WriteBgra(IntPtr pixels, int stride, int fullWidth, int fullHeight, int x, int y, int w, int h)
    {
        lock (_gate)
        {
            if (Resize(fullWidth, fullHeight)) (x, y, w, h) = (0, 0, fullWidth, fullHeight);
            for (var row = 0; row < h; row++)
                Marshal.Copy(pixels + (y + row) * stride + x * 4, _shadow, ((y + row) * fullWidth + x) * 4, w * 4);
            Touch(new PixelRect(x, y, w, h));
        }
        Schedule();
    }

    /// <summary>Convert a whole RGB565 frame (any thread).</summary>
    public void WriteRgb565(byte[] data, int offset, int w, int h)
    {
        lock (_gate)
        {
            Resize(w, h);
            var o = 0;
            for (var i = offset; i < offset + w * h * 2; i += 2, o += 4)
            {
                var p = data[i] | (data[i + 1] << 8);
                var r = (p >> 11) & 0x1F;
                var g = (p >> 5) & 0x3F;
                var b = p & 0x1F;
                _shadow[o] = (byte)((b << 3) | (b >> 2));
                _shadow[o + 1] = (byte)((g << 2) | (g >> 4));
                _shadow[o + 2] = (byte)((r << 3) | (r >> 2));
                _shadow[o + 3] = 255;
            }
            Touch(new PixelRect(0, 0, w, h));
        }
        Schedule();
    }

    /// <summary>Drop the picture (UI thread).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _shadow = [];
            (_w, _h) = (0, 0);
            _dirty = default;
            _pending = false;
        }
        Bitmap = null;
        Fps = 0;
        _presentTimes.Clear();
        Presented?.Invoke();
    }

    bool Resize(int w, int h)
    {
        if (_w == w && _h == h) return false;
        _shadow = new byte[w * h * 4];
        (_w, _h) = (w, h);
        _dirty = new PixelRect(0, 0, w, h);
        return true;
    }

    void Touch(PixelRect r) => _dirty = _dirty.Width == 0 ? r : _dirty.Union(r);

    void Schedule()
    {
        lock (_gate)
        {
            if (_pending) return;
            _pending = true;
        }
        Dispatcher.UIThread.Post(Present, DispatcherPriority.Render);
    }

    void Present()
    {
        PixelRect rect;
        byte[] shadow;
        int w, h;
        lock (_gate)
        {
            (rect, shadow, w, h) = (_dirty, _shadow, _w, _h);
            _dirty = default;
            _pending = false;
        }
        if (w <= 0 || h <= 0 || rect.Width <= 0) return;

        if (Bitmap is null || Bitmap.PixelSize.Width != w || Bitmap.PixelSize.Height != h)
        {
            Bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            rect = new PixelRect(0, 0, w, h);
        }

        using (var fb = Bitmap.Lock())
        {
            for (var row = rect.Y; row < rect.Y + rect.Height; row++)
                Marshal.Copy(shadow, (row * w + rect.X) * 4, fb.Address + row * fb.RowBytes + rect.X * 4, rect.Width * 4);
        }

        var now = Environment.TickCount64;
        _presentTimes.Enqueue(now);
        while (_presentTimes.Count > 0 && now - _presentTimes.Peek() > 2000) _presentTimes.Dequeue();
        Fps = _presentTimes.Count > 1 ? (_presentTimes.Count - 1) * 1000.0 / (now - _presentTimes.Peek()) : 0;

        Presented?.Invoke();
    }
}
