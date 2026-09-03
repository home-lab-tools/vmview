using Avalonia.Controls;
using Avalonia.Interactivity;
using VmView.ViewModels;

namespace VmView.Views;

public partial class ConsolePage : UserControl
{
    public ConsolePage() => InitializeComponent();

    // A double-click is the VM's while input is on; only the view-only stage treats it as "fullscreen".
    void OnStageDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConsolePageViewModel { InputEnabled: false } vm) vm.ToggleFullscreenCommand.Execute(null);
    }
}
