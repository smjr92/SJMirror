using SJMirror.App.Models;

namespace SJMirror.App.Services;

public interface IWirelessAdbService
{
    Task<WirelessPairingSession> CreateQrPairingSessionAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<WirelessPairingResult> WaitForQrPairingAsync(
        WirelessPairingSession session,
        CancellationToken cancellationToken = default);

    Task<WirelessPairingResult> PairWithCodeAsync(
        string ipAddress,
        int port,
        string pairingCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WirelessMdnsService>> GetMdnsServicesAsync(
        CancellationToken cancellationToken = default);

    Task ReconnectKnownDevicesAsync(CancellationToken cancellationToken = default);

    void Cancel(WirelessPairingSession session);
}
