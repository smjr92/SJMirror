namespace SJMirror.App.Models;

public sealed record WirelessMdnsService(
    string InstanceName,
    string ServiceType,
    string Endpoint)
{
    public bool IsPairingService =>
        ServiceType.Equals("_adb-tls-pairing._tcp", StringComparison.OrdinalIgnoreCase);

    public bool IsConnectService =>
        ServiceType.Equals("_adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase);
}
