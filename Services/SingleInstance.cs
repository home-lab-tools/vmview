namespace VmView.Services;

/// <summary>
/// One VmView per session. The first instance owns a named mutex and listens on a named event; a second
/// launch (the user double-clicks the exe while it sits in the tray) pulses the event and exits, and the
/// resident instance brings its window up.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    const string Name = @"Local\VmView.6C2A1F0E";

    readonly Mutex _mutex;
    readonly EventWaitHandle _show;
    readonly bool _owner;
    Thread? _listener;
    volatile bool _stopping;

    SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, Name + ".mutex", out _owner);
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".show");
    }

    public bool IsFirst => _owner;

    /// <summary>
    /// Claim the session. When another instance already runs, return null — after asking it to show its
    /// window when <paramref name="askToShow"/> is set (a tray-only launch, e.g. the logon task, stays quiet).
    /// </summary>
    public static SingleInstance? Acquire(bool askToShow)
    {
        var si = new SingleInstance();
        if (si.IsFirst) return si;
        if (askToShow) si._show.Set();
        si.Dispose();
        return null;
    }

    /// <summary>Invoke <paramref name="onShowRequested"/> (on a worker thread) every time another launch asks for the window.</summary>
    public void Listen(Action onShowRequested)
    {
        _listener = new Thread(() =>
        {
            while (!_stopping)
            {
                if (_show.WaitOne(500) && !_stopping) onShowRequested();
            }
        }) { IsBackground = true, Name = "single-instance" };
        _listener.Start();
    }

    public void Dispose()
    {
        _stopping = true;
        _show.Dispose();
        if (_owner) { try { _mutex.ReleaseMutex(); } catch (ApplicationException) { } }
        _mutex.Dispose();
    }
}
