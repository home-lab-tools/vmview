using Avalonia;
using VmView.Services;

namespace VmView;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var tray = args.Contains(Autostart.TrayArgument, StringComparer.OrdinalIgnoreCase);

        // One instance per session: a second launch hands over to the resident one (which brings its
        // window up unless this launch itself only wanted the tray) and leaves.
        using var instance = SingleInstance.Acquire(askToShow: !tray);
        if (instance is null) return 0;

        App.Instance = instance;
        App.StartInTray = tray;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
