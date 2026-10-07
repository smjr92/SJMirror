using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using SJMirror.App.Models;

namespace SJMirror.App.Services;

public sealed partial class WirelessAdbService : IWirelessAdbService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private readonly IProcessRunner _processRunner;
    private readonly ISensitiveProcessRunner _sensitiveProcessRunner;
    private readonly IAdbPathResolver _pathResolver;
    private readonly IWirelessQrCodeRenderer _qrCodeRenderer;
    private readonly WirelessPairingCredentialsGenerator _credentialsGenerator;
    private readonly WirelessMdnsServicesParser _mdnsParser;
    private readonly TimeSpan _discoveryInterval;
    private readonly object _sessionSync = new();
    private WirelessPairingSession? _activeSession;

    public WirelessAdbService()
        : this(
            new ProcessRunner(),
            new SensitiveProcessRunner(),
            new AdbPathResolver(),
            new WirelessQrCodeRenderer(),
            new WirelessPairingCredentialsGenerator(),
            new WirelessMdnsServicesParser(),
            TimeSpan.FromSeconds(1))
    {
    }

    public WirelessAdbService(
        IProcessRunner processRunner,
        ISensitiveProcessRunner sensitiveProcessRunner,
        IAdbPathResolver pathResolver,
        IWirelessQrCodeRenderer qrCodeRenderer,
        WirelessPairingCredentialsGenerator credentialsGenerator,
        WirelessMdnsServicesParser mdnsParser,
        TimeSpan discoveryInterval)
    {
        _processRunner = processRunner;
        _sensitiveProcessRunner = sensitiveProcessRunner;
        _pathResolver = pathResolver;
        _qrCodeRenderer = qrCodeRenderer;
        _credentialsGenerator = credentialsGenerator;
        _mdnsParser = mdnsParser;
        _discoveryInterval = discoveryInterval > TimeSpan.Zero
            ? discoveryInterval
            : throw new ArgumentOutOfRangeException(nameof(discoveryInterval));
    }

    public Task<WirelessPairingSession> CreateQrPairingSessionAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var credentials = _credentialsGenerator.Generate();
        var qrCode = _qrCodeRenderer.RenderPng(credentials.Payload);
        var session = new WirelessPairingSession(
            credentials.ServiceName,
            credentials.Secret,
            qrCode,
            DateTimeOffset.UtcNow.Add(timeout));
        lock (_sessionSync)
        {
            _activeSession?.Invalidate();
            _activeSession = session;
        }

        return Task.FromResult(session);
    }

    public async Task<WirelessPairingResult> WaitForQrPairingAsync(
        WirelessPairingSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsValid)
        {
            return WirelessPairingResult.Failure(
                WirelessPairingState.Cancelled,
                "The pairing session is no longer active.");
        }

        try
        {
            while (DateTimeOffset.UtcNow < session.ExpiresAt)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var services = await GetMdnsServicesAsync(cancellationToken).ConfigureAwait(false);
                var pairingService = services.FirstOrDefault(service =>
                    service.IsPairingService &&
                    service.InstanceName.Equals(session.ServiceName, StringComparison.Ordinal));
                if (pairingService is not null)
                {
                    var result = await PairAsync(
                            pairingService.Endpoint,
                            session.GetSecret(),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!result.IsSuccess)
                    {
                        return result;
                    }

                    await ReconnectKnownDevicesAsync(cancellationToken).ConfigureAwait(false);
                    return WirelessPairingResult.Success;
                }

                var remaining = session.ExpiresAt - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(
                            remaining < _discoveryInterval ? remaining : _discoveryInterval,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return WirelessPairingResult.Failure(
                WirelessPairingState.TimedOut,
                "Pairing timed out. Generate a new QR code and try again.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WirelessPairingResult.Failure(
                WirelessPairingState.Cancelled,
                "Pairing was cancelled.");
        }
        finally
        {
            InvalidateSession(session);
        }
    }

    public async Task<WirelessPairingResult> PairWithCodeAsync(
        string ipAddress,
        int port,
        string pairingCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedPairingCode = pairingCode ?? string.Empty;
        if (!IPAddress.TryParse(ipAddress, out var address) || !IsLocalAddress(address))
        {
            return WirelessPairingResult.Failure(
                WirelessPairingState.Failed,
                "Enter a valid local network IP address.");
        }

        if (port is < 1 or > 65535 || !PairingCodePattern().IsMatch(normalizedPairingCode))
        {
            return WirelessPairingResult.Failure(
                WirelessPairingState.Failed,
                "Enter a valid pairing port and six-digit code.");
        }

        var endpoint = address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";
        var result = await PairAsync(endpoint, normalizedPairingCode, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await ReconnectKnownDevicesAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<IReadOnlyList<WirelessMdnsService>> GetMdnsServicesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(["mdns", "services"], cancellationToken)
            .ConfigureAwait(false);
        return result.ExitCode == 0
            ? _mdnsParser.Parse(result.StandardOutput)
            : [];
    }

    public async Task ReconnectKnownDevicesAsync(CancellationToken cancellationToken = default)
    {
        var check = await ExecuteAsync(["mdns", "check"], cancellationToken).ConfigureAwait(false);
        if (check.ExitCode != 0)
        {
            return;
        }

        var services = await GetMdnsServicesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var service in services.Where(service => service.IsConnectService))
        {
            await ExecuteAsync(["connect", service.Endpoint], cancellationToken).ConfigureAwait(false);
        }
    }

    public void Cancel(WirelessPairingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        InvalidateSession(session);
    }

    private void InvalidateSession(WirelessPairingSession session)
    {
        lock (_sessionSync)
        {
            session.Invalidate();
            if (ReferenceEquals(_activeSession, session))
            {
                _activeSession = null;
            }
        }
    }

    private async Task<WirelessPairingResult> PairAsync(
        string endpoint,
        string secret,
        CancellationToken cancellationToken)
    {
        var resolution = _pathResolver.Resolve();
        if (!resolution.IsResolved)
        {
            return WirelessPairingResult.Failure(
                WirelessPairingState.Failed,
                "ADB was not found.");
        }

        try
        {
            var result = await _sensitiveProcessRunner.RunAsync(
                    resolution.ExecutablePath!,
                    ["pair", endpoint],
                    secret,
                    CommandTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.ExitCode == 0
                ? WirelessPairingResult.Success
                : WirelessPairingResult.Failure(
                    WirelessPairingState.Failed,
                    "Unable to pair with the Android device.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WirelessPairingResult.Failure(
                WirelessPairingState.Cancelled,
                "Pairing was cancelled.");
        }
        catch
        {
            return WirelessPairingResult.Failure(
                WirelessPairingState.Failed,
                "Unable to pair with the Android device.");
        }
    }

    private async Task<ProcessResult> ExecuteAsync(
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        var resolution = _pathResolver.Resolve();
        if (!resolution.IsResolved)
        {
            return new ProcessResult(-1, string.Empty, "ADB was not found.");
        }

        try
        {
            return await _processRunner.RunAsync(
                    resolution.ExecutablePath!,
                    arguments,
                    CommandTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new ProcessResult(-1, string.Empty, "ADB command failed.");
        }
    }

    private static bool IsLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 127 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
               bytes[0] == 169 && bytes[1] == 254;
    }

    [GeneratedRegex("^[0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex PairingCodePattern();
}
