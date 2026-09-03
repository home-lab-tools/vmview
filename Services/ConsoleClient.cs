using System.Runtime.InteropServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VmView.Rendering;

namespace VmView.Services;

public enum ConsoleState { Idle, Connecting, Connected, Disconnected, Failed }

/// <summary>
/// One Hyper-V console session through the native vmrdp shim (FreeRDP inside). vmms on the host ends the
/// stream — the guest needs no RDP, no network, no integration services — and signs the current user in
/// through the native SSPI, exactly as VMConnect does. The shim exports no input entry point, so the
/// session is a picture by construction.
/// </summary>
public sealed partial class ConsoleClient : ObservableObject, IDisposable, Controls.IConsoleInput
{
    const string Lib = "vmrdp";
    const int ConsolePort = 2179;

    /// <summary>FREERDP_ERROR_CONNECT_LOGON_FAILURE — the current user is not allowed on that console.</summary>
    public const uint LogonFailure = 0x00020014;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void FrameCallback(IntPtr user, IntPtr pixels, int width, int height, int stride, int x, int y, int w, int h);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void StatusCallback(IntPtr user, int state, uint code, [MarshalAs(UnmanagedType.LPStr)] string text);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr vmrdp_open([MarshalAs(UnmanagedType.LPUTF8Str)] string host, int port,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string vmId, FrameCallback onFrame, StatusCallback onStatus, IntPtr userData);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void vmrdp_close(IntPtr session);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr vmrdp_version();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern void vmrdp_set_input(IntPtr session, int enabled);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int vmrdp_mouse(IntPtr session, uint flags, int x, int y);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    static extern int vmrdp_key(IntPtr session, int down, uint code, int extended);

    public static string NativeVersion
    {
        get { try { return "FreeRDP " + Marshal.PtrToStringAnsi(vmrdp_version()); } catch (Exception ex) { return ex.Message; } }
    }

    readonly FrameCallback _frameKeepAlive;     // delegates must outlive the native session
    readonly StatusCallback _statusKeepAlive;
    IntPtr _handle;

    [ObservableProperty] ConsoleState _state;
    [ObservableProperty] string _message = "";
    [ObservableProperty] uint _lastError;
    [ObservableProperty] int _width;
    [ObservableProperty] int _height;

    /// <summary>The input gate. Shut by default; the shim drops every mouse/key call while it is shut.</summary>
    [ObservableProperty] bool _inputEnabled;

    partial void OnInputEnabledChanged(bool value)
    {
        if (_handle != IntPtr.Zero) vmrdp_set_input(_handle, value ? 1 : 0);
    }

    public void Mouse(ushort flags, int x, int y)
    {
        if (_handle != IntPtr.Zero && InputEnabled) vmrdp_mouse(_handle, flags, x, y);
    }

    public void Key(bool down, byte code, bool extended)
    {
        if (_handle != IntPtr.Zero && InputEnabled) vmrdp_key(_handle, down ? 1 : 0, code, extended ? 1 : 0);
    }

    public ConsoleClient()
    {
        _frameKeepAlive = OnFrame;
        _statusKeepAlive = OnStatus;
    }

    public FrameBuffer Frame { get; } = new();
    public bool IsOpen => _handle != IntPtr.Zero;

    /// <summary>Start a session; the shim's own thread connects and pumps.</summary>
    public void Open(string host, string vmId)
    {
        Close();
        LastError = 0;
        State = ConsoleState.Connecting;
        Message = "";
        _handle = vmrdp_open(host is "." or "localhost" ? "127.0.0.1" : host, ConsolePort, vmId, _frameKeepAlive, _statusKeepAlive, IntPtr.Zero);
        if (_handle == IntPtr.Zero)
        {
            State = ConsoleState.Failed;
            Message = "the native console client could not start";
            return;
        }
        if (InputEnabled) vmrdp_set_input(_handle, 1);
    }

    public void Close()
    {
        var h = _handle;
        _handle = IntPtr.Zero;
        if (h != IntPtr.Zero) vmrdp_close(h);
        State = ConsoleState.Idle;
        Frame.Clear();
    }

    // ----- native callbacks, session thread -----------------------------------------------------------------

    void OnFrame(IntPtr user, IntPtr pixels, int width, int height, int stride, int x, int y, int w, int h)
        => Frame.WriteBgra(pixels, stride, width, height, x, y, w, h);

    void OnStatus(IntPtr user, int state, uint code, string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (state)
            {
                case 1: State = ConsoleState.Connecting; break;
                case 2:
                case 3:
                    (Width, Height) = ((int)(code >> 16), (int)(code & 0xFFFF));
                    State = ConsoleState.Connected;
                    break;
                case 4:
                    LastError = code;
                    Message = code == 0 ? "disconnected" : Describe(code, text);
                    State = ConsoleState.Disconnected;
                    break;
                case 5:
                    LastError = code;
                    Message = Describe(code, text);
                    State = ConsoleState.Failed;
                    break;
            }
        });
    }

    static string Describe(uint code, string text) => code == LogonFailure
        ? "the console refused the current user — Hyper-V administrator rights are required"
        : $"{text} (0x{code:X8})";

    public void Dispose() => Close();
}
