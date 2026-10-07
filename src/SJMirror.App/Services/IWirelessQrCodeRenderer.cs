namespace SJMirror.App.Services;

public interface IWirelessQrCodeRenderer
{
    byte[] RenderPng(string payload);
}
