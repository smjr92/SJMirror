using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class WirelessQrCodeRendererTests
{
    [Fact]
    public void RenderPngProducesPngImage()
    {
        var bytes = new WirelessQrCodeRenderer().RenderPng(
            "WIFI:T:ADB;S:studio-AbCd123456;P:Secret1234;;");

        Assert.True(bytes.Length > 8);
        Assert.Equal(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            bytes.Take(8));
    }
}
