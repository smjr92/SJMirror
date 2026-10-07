using QRCoder;

namespace SJMirror.App.Services;

public sealed class WirelessQrCodeRenderer : IWirelessQrCodeRenderer
{
    public byte[] RenderPng(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        using var data = QRCodeGenerator.GenerateQrCode(
            payload,
            QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(data);
        return qrCode.GetGraphic(12);
    }
}
