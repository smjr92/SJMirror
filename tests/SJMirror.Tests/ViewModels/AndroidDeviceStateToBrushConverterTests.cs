using System.Globalization;
using System.Windows.Media;
using SJMirror.App.Models;
using SJMirror.App.ViewModels;

namespace SJMirror.Tests.ViewModels;

public sealed class AndroidDeviceStateToBrushConverterTests
{
    private readonly Brush _connected = new SolidColorBrush(Color.FromRgb(1, 2, 3));
    private readonly Brush _unauthorized = new SolidColorBrush(Color.FromRgb(4, 5, 6));
    private readonly Brush _offline = new SolidColorBrush(Color.FromRgb(7, 8, 9));
    private readonly Brush _unknown = new SolidColorBrush(Color.FromRgb(10, 11, 12));

    [Theory]
    [InlineData(AndroidDeviceState.Device, "connected")]
    [InlineData(AndroidDeviceState.Unauthorized, "unauthorized")]
    [InlineData(AndroidDeviceState.Offline, "offline")]
    [InlineData(AndroidDeviceState.Unknown, "unknown")]
    [InlineData(AndroidDeviceState.Disconnected, "unknown")]
    public void ConvertMapsDeviceStateToConfiguredIndicator(
        AndroidDeviceState state,
        string expected)
    {
        var converter = new AndroidDeviceStateToBrushConverter
        {
            ConnectedBrush = _connected,
            UnauthorizedBrush = _unauthorized,
            OfflineBrush = _offline,
            UnknownBrush = _unknown
        };

        var result = converter.Convert(state, typeof(Brush), null!, CultureInfo.InvariantCulture);

        Assert.Same(ExpectedBrush(expected), result);
    }

    private Brush ExpectedBrush(string name) => name switch
    {
        "connected" => _connected,
        "unauthorized" => _unauthorized,
        "offline" => _offline,
        _ => _unknown
    };
}
