using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VmView.Services;

namespace VmView.ViewModels;

/// <summary>A cell of the wall with no VM in it.</summary>
public sealed class EmptyCell;

/// <summary>
/// The root page — a wall of live previews, one tile per running VM, like a bank of security cameras.
/// The wall is a fixed columns × rows grid (2 × 2 at least) that fills the page whatever the count; the
/// tiles scale to the cell and cells with no VM show as blanks. A view over the shell's
/// <see cref="VmCatalog"/>: items are the catalog's own, this only filters them and lays them out.
/// Previews are requested at the tile's own size so a big tile is not a blown-up thumbnail.
/// </summary>
public sealed partial class ListPageViewModel : ObservableObject, IDisposable
{
    public const int MinAxis = 2, MaxAxis = 8;

    readonly VmCatalog _catalog;
    readonly Action<VmItem> _open;
    int _previewWidth;

    public ListPageViewModel(VmCatalog catalog, string hostsText, Action<VmItem> open)
    {
        _catalog = catalog;
        _open = open;
        HostsText = hostsText;
        catalog.All.CollectionChanged += OnCatalogChanged;
        catalog.PropertyChanged += OnCatalogPropertyChanged;
        foreach (var v in catalog.All) v.PropertyChanged += OnItemChanged;
        Rebuild();
    }

    /// <summary>Running VMs (those with a screen), in catalog order.</summary>
    public ObservableCollection<VmItem> Vms { get; } = [];

    /// <summary>What the wall shows: the VMs first, then <see cref="EmptyCell"/>s up to columns × rows.</summary>
    public ObservableCollection<object> Cells { get; } = [];

    public string HostsText { get; }
    public IReadOnlyList<int> AxisOptions { get; } = Enumerable.Range(MinAxis, MaxAxis - MinAxis + 1).ToList();

    [ObservableProperty] int _columns = MinAxis;
    [ObservableProperty] int _rows = MinAxis;

    public bool Loading => !_catalog.Loaded;
    public bool Empty => _catalog.Loaded && Vms.Count == 0;

    partial void OnColumnsChanged(int value) => Fill();
    partial void OnRowsChanged(int value) => Fill();

    /// <summary>Set by the view from the tile's pixel width, so the host renders previews at that size.</summary>
    public int PreviewWidth
    {
        get => _previewWidth;
        set
        {
            if (_previewWidth == value) return;
            _previewWidth = value;
            foreach (var v in Vms) v.PreviewWidth = value;
        }
    }

    [RelayCommand] void Open(VmItem? vm) { if (vm is not null) _open(vm); }
    [RelayCommand] void OpenFirst() { if (Vms.Count > 0) _open(Vms[0]); }

    void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VmCatalog.Loaded)) { OnPropertyChanged(nameof(Loading)); OnPropertyChanged(nameof(Empty)); }
    }

    void OnCatalogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (var v in e.OldItems.OfType<VmItem>()) v.PropertyChanged -= OnItemChanged;
        if (e.NewItems is not null) foreach (var v in e.NewItems.OfType<VmItem>()) v.PropertyChanged += OnItemChanged;
        Rebuild();
    }

    // A VM starting or stopping changes whether it belongs on the wall.
    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VmItem.HasConsole)) Rebuild();
    }

    /// <summary>Re-derive the VM list from the catalog, moving/inserting/removing rather than rebuilding.</summary>
    void Rebuild()
    {
        var wanted = _catalog.All.Where(v => v.HasConsole).ToList();
        var index = 0;
        foreach (var v in wanted)
        {
            var at = Vms.IndexOf(v);
            if (at < 0) { v.PreviewWidth = _previewWidth; Vms.Insert(index, v); Cells.Insert(index, v); }
            else if (at != index) { Vms.Move(at, index); Cells.Move(at, index); }
            index++;
        }
        for (var i = Vms.Count - 1; i >= index; i--) { Vms.RemoveAt(i); Cells.RemoveAt(i); }

        OnPropertyChanged(nameof(Empty));
        Fill();
    }

    /// <summary>Pad (or trim) the blanks after the VMs so the wall always holds exactly columns × rows cells.</summary>
    void Fill()
    {
        var total = Math.Max(Columns * Rows, Vms.Count);
        while (Cells.Count < total) Cells.Add(new EmptyCell());
        while (Cells.Count > total && Cells[^1] is EmptyCell) Cells.RemoveAt(Cells.Count - 1);
    }

    public void Dispose()
    {
        _catalog.All.CollectionChanged -= OnCatalogChanged;
        _catalog.PropertyChanged -= OnCatalogPropertyChanged;
        foreach (var v in _catalog.All) v.PropertyChanged -= OnItemChanged;
    }
}
