using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using VmView.ViewModels;

namespace VmView.Views;

public partial class ListPage : UserControl
{
    public ListPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => PushPreviewSize();
        Wall.SizeChanged += (_, _) => PushPreviewSize();
    }

    ListPageViewModel? Vm => DataContext as ListPageViewModel;

    /// <summary>A click (left button) on a tile opens that VM.</summary>
    void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if ((sender as Control)?.DataContext is VmItem vm) { Vm?.OpenCommand.Execute(vm); e.Handled = true; }
    }

    /// <summary>Ask the host for previews at the tile's own pixel width, so a big tile is not a blown-up thumbnail.</summary>
    void PushPreviewSize()
    {
        if (Vm is null || Wall.Bounds.Width <= 0) return;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var cols = Math.Max(1, Vm.Columns);
        Vm.PreviewWidth = (int)Math.Ceiling(Wall.Bounds.Width / cols * scaling);
        Vm.PropertyChanged -= OnVmChanged;
        Vm.PropertyChanged += OnVmChanged;
    }

    void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ListPageViewModel.Columns)) PushPreviewSize();
    }
}
