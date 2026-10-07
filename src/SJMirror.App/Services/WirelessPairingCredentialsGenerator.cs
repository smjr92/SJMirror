using System.Security.Cryptography;
using System.Text;

namespace SJMirror.App.Services;

public sealed class WirelessPairingCredentialsGenerator
{
    private const string Alphabet =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public (string ServiceName, byte[] Secret, string Payload) Generate()
    {
        var random = GenerateRandomText(10);
        var serviceName = $"studio-{random}";
        var secretText = GenerateRandomText(10);
        var secret = Encoding.ASCII.GetBytes(secretText);
        var payload = $"WIFI:T:ADB;S:{serviceName};P:{secretText};;";
        return (serviceName, secret, payload);
    }

    private static string GenerateRandomText(int length)
    {
        Span<char> result = stackalloc char[length];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(result);
    }
}
