using SJMirror.App.Models;
using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class AdbDevicesParserTests
{
    private readonly AdbDevicesParser _parser = new();

    [Theory]
    [InlineData("192.168.1.40:39877")]
    [InlineData("adb-ABC123-XyZ._adb-tls-connect._tcp")]
    public void DetectConnectionTypeRecognizesWirelessSerials(string serial)
    {
        Assert.Equal(
            AndroidConnectionType.Network,
            AdbDevicesParser.DetectConnectionType(serial));
    }

    [Fact]
    public void ParseReturnsEmptyListWhenNoDevicesAreAttached()
    {
        var result = _parser.Parse("List of devices attached\r\n\r\n");

        Assert.True(result.IsValid);
        Assert.Empty(result.Devices);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ParseReadsOneConnectedDeviceAndAdditionalProperties()
    {
        const string output =
            "List of devices attached\r\n" +
            "R58M123ABC device product:dm3q model:SM_S931B device:dm3q transport_id:1\r\n";

        var result = _parser.Parse(output);

        var device = Assert.Single(result.Devices);
        Assert.Equal("R58M123ABC", device.Serial);
        Assert.Equal(AndroidDeviceState.Device, device.State);
        Assert.Equal("SM S931B", device.Model);
        Assert.Equal(AndroidConnectionType.Usb, device.ConnectionType);
    }

    [Fact]
    public void ParseReadsMultipleDevices()
    {
        const string output =
            "List of devices attached\n" +
            "R58M123ABC device transport_id:1\n" +
            "192.168.1.20:5555 device transport_id:2\n";

        var result = _parser.Parse(output);

        Assert.Collection(
            result.Devices,
            device => Assert.Equal(AndroidConnectionType.Usb, device.ConnectionType),
            device => Assert.Equal(AndroidConnectionType.Network, device.ConnectionType));
    }

    [Theory]
    [InlineData("unauthorized", AndroidDeviceState.Unauthorized)]
    [InlineData("offline", AndroidDeviceState.Offline)]
    [InlineData("unexpected-state", AndroidDeviceState.Unknown)]
    public void ParseMapsDeviceStates(string adbState, AndroidDeviceState expectedState)
    {
        var result = _parser.Parse($"List of devices attached\nABC123 {adbState} transport_id:1\n");

        Assert.Equal(expectedState, Assert.Single(result.Devices).State);
    }

    [Fact]
    public void ParseIgnoresMalformedDeviceLineWithWarning()
    {
        const string output = "List of devices attached\nmalformed-line\n";

        var result = _parser.Parse(output);

        Assert.True(result.IsValid);
        Assert.Empty(result.Devices);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void ParseReturnsControlledErrorForUnexpectedOutput()
    {
        var result = _parser.Parse("unexpected adb response");

        Assert.False(result.IsValid);
        Assert.Empty(result.Devices);
        Assert.NotNull(result.ErrorMessage);
    }
}
