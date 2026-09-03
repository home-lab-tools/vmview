using System.Text.Json;

namespace VmView.Models;

/// <summary>Settings read from vmview.json beside the exe; every field has a working default.</summary>
public sealed class Options
{
    /// <summary>Hyper-V hosts to browse. "." is this machine; a remote host needs WMI access and console rights for the current user.</summary>
    public string[] Hosts { get; set; } = ["."];

    /// <summary>Sidebar preview rate. Each grab costs the host a few ms at tile size.</summary>
    public double TileFps { get; set; } = 1;
    public int TileWidth { get; set; } = 320;

    /// <summary>How often the VM list and its counters are re-read.</summary>
    public double InventorySeconds { get; set; } = 2;

    /// <summary>
    /// The exe the user launched. Inside a single-file publish AppContext.BaseDirectory is the bundle's
    /// extraction folder, so anything meant to sit "beside the exe" — the config, the autostart entry —
    /// is keyed on the process path instead.
    /// </summary>
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "VmView.exe");
    public static string ExeDirectory => Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;

    public static Options Load()
    {
        var path = Path.Combine(ExeDirectory, "vmview.json");
        if (!File.Exists(path)) return new Options();
        try
        {
            return JsonSerializer.Deserialize<Options>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip })
                ?? new Options();
        }
        catch (JsonException)
        {
            return new Options();
        }
    }
}
