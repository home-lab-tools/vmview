using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VmView.Models;
using VmView.ViewModels;

namespace VmView.Services;

/// <summary>
/// Every VM on the configured hosts as a long-lived <see cref="VmItem"/> each, re-read on a timer and
/// folded into <see cref="All"/> in place, so a page holding an item keeps following its VM through
/// state changes. Polling and previews are switched by the shell to match what is on screen.
/// </summary>
public sealed partial class VmCatalog : ObservableObject, IDisposable
{
    readonly Options _opt;
    readonly HyperVInventory _inventory;
    readonly DispatcherTimer _timer;
    bool _busy;
    bool _previews = true;

    public VmCatalog(Options opt)
    {
        _opt = opt;
        _inventory = new HyperVInventory(opt);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(Math.Max(0.5, opt.InventorySeconds)), DispatcherPriority.Background, (_, _) => Refresh());
    }

    /// <summary>All VMs, hosts then names; items are reused across reads.</summary>
    public ObservableCollection<VmItem> All { get; } = [];

    /// <summary>The first inventory has arrived (or failed) — until then the list is "loading", not "empty".</summary>
    [ObservableProperty] bool _loaded;

    /// <summary>Last per-host failure, cleared by the next clean pass.</summary>
    [ObservableProperty] string? _error;

    /// <summary>Re-read the hosts every <see cref="Options.InventorySeconds"/>; off while the app sits in the tray.</summary>
    public bool Polling
    {
        get => _timer.IsEnabled;
        set
        {
            if (value == _timer.IsEnabled) return;
            if (value) { _timer.Start(); Refresh(); } else _timer.Stop();
        }
    }

    /// <summary>Whether the cards' preview workers may run (only while the list page is on screen).</summary>
    public bool PreviewsEnabled
    {
        get => _previews;
        set
        {
            if (_previews == value) return;
            _previews = value;
            foreach (var v in All) v.PreviewsEnabled = value;
        }
    }

    public async void Refresh()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var list = await Task.Run(_inventory.Collect);
            Error = _inventory.LastError;
            Merge(list);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { _busy = false; Loaded = true; }
    }

    /// <summary>Fold a fresh read into <see cref="All"/> without rebuilding it.</summary>
    void Merge(IReadOnlyList<VmSummary> fresh)
    {
        var byKey = All.ToDictionary(v => v.Key);
        var keep = new HashSet<string>();
        var index = 0;
        foreach (var s in fresh)
        {
            keep.Add(s.Key);
            if (byKey.TryGetValue(s.Key, out var item))
            {
                item.Apply(s);
                var at = All.IndexOf(item);
                if (at != index) All.Move(at, index);
            }
            else
            {
                All.Insert(index, new VmItem(_opt, s) { PreviewsEnabled = _previews });
            }
            index++;
        }

        foreach (var gone in All.Where(v => !keep.Contains(v.Key)).ToList())
        {
            All.Remove(gone);
            gone.Dispose();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var v in All) v.Dispose();
    }
}
