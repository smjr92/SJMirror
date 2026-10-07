namespace SJMirror.App.Services;

public interface IAdbService
{
    Task<AdbAvailabilityResult> CheckAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> GetVersionAsync(
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> StartServerAsync(
        CancellationToken cancellationToken = default);

    Task<AdbCommandResult> KillServerAsync(
        CancellationToken cancellationToken = default);

    Task<AdbDevicesResult> GetDevicesAsync(
        CancellationToken cancellationToken = default);

    Task<AdbDeviceInfoResult> GetDeviceInfoAsync(
        string serial,
        CancellationToken cancellationToken = default);
}
