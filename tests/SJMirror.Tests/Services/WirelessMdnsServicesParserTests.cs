using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class WirelessMdnsServicesParserTests
{
    [Fact]
    public void ParseRecognizesPairingAndConnectServices()
    {
        const string output = """
            List of discovered mdns services
            studio-AbCd123456 _adb-tls-pairing._tcp 192.168.1.40:37125
            adb-device-guid _adb-tls-connect._tcp 192.168.1.40:39877
            unrelated _http._tcp 192.168.1.20:80
            """;

        var services = new WirelessMdnsServicesParser().Parse(output);

        Assert.Equal(2, services.Count);
        Assert.True(services[0].IsPairingService);
        Assert.Equal("studio-AbCd123456", services[0].InstanceName);
        Assert.Equal("192.168.1.40:37125", services[0].Endpoint);
        Assert.True(services[1].IsConnectService);
    }

    [Fact]
    public void ParseIgnoresEmptyAndUnexpectedLines()
    {
        var services = new WirelessMdnsServicesParser().Parse(
            "List of discovered mdns services\r\ninvalid line\r\n");

        Assert.Empty(services);
    }
}
