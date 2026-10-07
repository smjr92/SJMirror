using SJMirror.App.Models;

namespace SJMirror.App.Services;

public interface IMirrorService : IAsyncDisposable
{
    event EventHandler<MirrorSessionEndedEventArgs>? SessionEnded;

    bool IsRunning { get; }

    string? ActiveDeviceSerial { get; }

    Task<MirrorOperationResult> StartAsync(
        AndroidDevice device,
        CancellationToken cancellationToken = default);

    Task<MirrorOperationResult> StartAsync(
        AndroidDevice device,
        MirrorSettings settings,
        CancellationToken cancellationToken = default) =>
        StartAsync(device, cancellationToken);

    Task<MirrorOperationResult> StopAsync(
        CancellationToken cancellationToken = default);
}
