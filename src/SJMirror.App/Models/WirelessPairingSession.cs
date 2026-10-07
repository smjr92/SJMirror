using System.Security.Cryptography;

namespace SJMirror.App.Models;

public sealed class WirelessPairingSession
{
    private byte[]? _secret;

    internal WirelessPairingSession(
        string serviceName,
        byte[] secret,
        byte[] qrCodePng,
        DateTimeOffset expiresAt)
    {
        ServiceName = serviceName;
        _secret = secret;
        QrCodePng = qrCodePng;
        ExpiresAt = expiresAt;
    }

    public string ServiceName { get; }

    public byte[] QrCodePng { get; }

    public DateTimeOffset ExpiresAt { get; }

    internal bool IsValid => _secret is not null;

    internal string GetSecret()
    {
        ObjectDisposedException.ThrowIf(_secret is null, this);
        return System.Text.Encoding.ASCII.GetString(_secret);
    }

    internal void Invalidate()
    {
        if (_secret is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_secret);
        _secret = null;
    }
}
