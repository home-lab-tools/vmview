using System.Collections.Concurrent;
using System.Management;
using VmView.Models;

namespace VmView.Services;

/// <summary>
/// Reads every configured host's VM list through one GetSummaryInformation call per host and joins in
/// the virtual monitor size from Msvm_VideoHead. Read-only WMI; nothing here can change a VM.
/// </summary>
public sealed class HyperVInventory
{
    // Msvm_SummaryInformation.RequestedInformation codes.
    static readonly ushort[] Wanted = [0, 1, 4, 100, 101, 103, 104, 105, 106];

    readonly Options _opt;
    readonly ConcurrentDictionary<string, ManagementScope> _scopes = new(StringComparer.OrdinalIgnoreCase);

    public HyperVInventory(Options opt) => _opt = opt;

    /// <summary>Last per-host failure, cleared by the next clean pass.</summary>
    public string? LastError { get; private set; }

    /// <summary>Blocking; call from a worker thread.</summary>
    public IReadOnlyList<VmSummary> Collect()
    {
        var all = new List<VmSummary>();
        string? error = null;

        foreach (var host in _opt.Hosts)
        {
            try { all.AddRange(CollectHost(host)); }
            catch (Exception ex)
            {
                _scopes.TryRemove(host, out _);
                error = $"{host}: {ex.Message}";
            }
        }

        LastError = error;
        return all.OrderBy(v => v.Host).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string ScopePath(string host)
        => host is "." or "localhost" ? @"\\.\root\virtualization\v2" : $@"\\{host}\root\virtualization\v2";

    ManagementScope ScopeFor(string host) => _scopes.GetOrAdd(host, h =>
    {
        var scope = new ManagementScope(ScopePath(h));
        scope.Connect();
        return scope;
    });

    IEnumerable<VmSummary> CollectHost(string host)
    {
        var scope = ScopeFor(host);
        var heads = VideoHeads(scope);

        using var svc = ManagementService(scope);
        using var inParams = svc.GetMethodParameters("GetSummaryInformation");
        inParams["SettingData"] = null;                 // null = every VM on the host
        inParams["RequestedInformation"] = Wanted;
        using var outParams = svc.InvokeMethod("GetSummaryInformation", inParams, null);

        if (outParams["SummaryInformation"] is not ManagementBaseObject[] infos) yield break;

        foreach (var info in infos)
        {
            using (info)
            {
                var id = info["Name"] as string;
                if (string.IsNullOrEmpty(id)) continue;
                heads.TryGetValue(id, out var head);

                yield return new VmSummary(
                    Host: host,
                    Id: id,
                    Name: info["ElementName"] as string ?? id,
                    State: (VmState)U16(info["EnabledState"]),
                    ProcessorCount: U16(info["NumberOfProcessors"]),
                    ProcessorLoad: U16(info["ProcessorLoad"]),
                    MemoryMb: (long)U64(info["MemoryUsage"]),
                    UpTime: TimeSpan.FromMilliseconds(U64(info["UpTime"])),
                    Heartbeat: (Heartbeat)U16(info["Heartbeat"]),
                    GuestOs: info["GuestOperatingSystem"] as string,
                    ScreenWidth: head.W,
                    ScreenHeight: head.H);
            }
        }
    }

    /// <summary>Current resolution of each VM's synthetic monitor, keyed by VM id.</summary>
    static Dictionary<string, (int W, int H)> VideoHeads(ManagementScope scope)
    {
        var map = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        using var search = new ManagementObjectSearcher(scope, new ObjectQuery(
            "SELECT SystemName, CurrentHorizontalResolution, CurrentVerticalResolution FROM Msvm_VideoHead"));
        foreach (var o in search.Get().Cast<ManagementObject>())
        {
            using (o)
            {
                if (o["SystemName"] is not string sys) continue;
                var w = (int)U32(o["CurrentHorizontalResolution"]);
                var h = (int)U32(o["CurrentVerticalResolution"]);
                if (w > 0 && h > 0) map[sys] = (w, h);
            }
        }
        return map;
    }

    internal static ManagementObject ManagementService(ManagementScope scope)
    {
        using var search = new ManagementObjectSearcher(scope, new ObjectQuery(
            "SELECT * FROM Msvm_VirtualSystemManagementService"));
        return search.Get().Cast<ManagementObject>().First();
    }

    static ushort U16(object? v) => v is null ? (ushort)0 : Convert.ToUInt16(v);
    static uint U32(object? v) => v is null ? 0u : Convert.ToUInt32(v);
    static ulong U64(object? v) => v is null ? 0ul : Convert.ToUInt64(v);
}
