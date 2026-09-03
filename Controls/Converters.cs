using System.Globalization;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;

namespace VmView.Controls;

/// <summary>Fit needs a finite viewport, so the stage's scrollbars are off for it and on for the pixel zooms.</summary>
public sealed class ZoomToScrollBars : IValueConverter
{
    public static readonly ZoomToScrollBars Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ScreenZoom.Fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
