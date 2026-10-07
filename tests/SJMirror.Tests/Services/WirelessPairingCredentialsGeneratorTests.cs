using System.Text;
using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class WirelessPairingCredentialsGeneratorTests
{
    [Fact]
    public void GenerateCreatesValidAdbQrPayload()
    {
        var credentials = new WirelessPairingCredentialsGenerator().Generate();
        var secret = Encoding.ASCII.GetString(credentials.Secret);

        Assert.StartsWith("studio-", credentials.ServiceName);
        Assert.Equal(17, credentials.ServiceName.Length);
        Assert.Equal(10, secret.Length);
        Assert.Equal(
            $"WIFI:T:ADB;S:{credentials.ServiceName};P:{secret};;",
            credentials.Payload);
    }

    [Fact]
    public void GenerateDoesNotReuseServiceNameOrSecret()
    {
        var generator = new WirelessPairingCredentialsGenerator();
        var first = generator.Generate();
        var second = generator.Generate();

        Assert.NotEqual(first.ServiceName, second.ServiceName);
        Assert.NotEqual(
            Encoding.ASCII.GetString(first.Secret),
            Encoding.ASCII.GetString(second.Secret));
    }
}
