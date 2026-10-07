using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SJMirror.App.Models;

namespace SJMirror.App.ViewModels;

public sealed class AndroidDeviceStateToBrushConverter : IValueConverter
{
    public Brush ConnectedBrush { get; set; } = Brushes.Green;

    public Brush UnauthorizedBrush { get; set; } = Brushes.Goldenrod;

    public Brush OfflineBrush { get; set; } = Brushes.IndianRed;

    public Brush UnknownBrush { get; set; } = Brushes.Gray;

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) => value switch
    {
        AndroidDeviceState.Device => ConnectedBrush,
        AndroidDeviceState.Unauthorized => UnauthorizedBrush,
        AndroidDeviceState.Offline => OfflineBrush,
        _ => UnknownBrush
    };

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) => Binding.DoNothing;
}
