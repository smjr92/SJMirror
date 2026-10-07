using SJMirror.App.Models;

namespace SJMirror.App.Services;

public sealed class AdbService : IAdbService
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(15);

    private readonly IProcessRunner _processRunner;
    private readonly IAdbPathResolver _pathResolver;
    private readonly AdbDevicesParser _devicesParser;

    public AdbService()
        : this(new ProcessRunner(), new AdbPathResolver())
    {
    }

    public AdbService(string adbExecutablePath)
        : this(new ProcessRunner(), new AdbPathResolver(adbExecutablePath))
    {
    }

    public AdbService(IProcessRunner processRunner, IAdbPathResolver pathResolver)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _devicesParser = new AdbDevicesParser();
    }

    public async Task<AdbAvailabilityResult> CheckAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var resolution = _pathResolver.Resolve();
        if (!resolution.IsResolved)
        {
            return new AdbAvailabilityResult(false, null, resolution.ErrorMessage);
        }

        var result = await ExecuteResolvedAsync(
                resolution.ExecutablePath!,
                ["version"],
                cancellationToken)
            .ConfigureAwait(false);

        return new AdbAvailabilityResult(
            result.IsSuccess,
            resolution.ExecutablePath,
            result.IsSuccess ? null : result.ErrorMessage);
    }

    public Task<AdbCommandResult> GetVersionAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(["version"], cancellationToken);

    public Task<AdbCommandResult> StartServerAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(["start-server"], cancellationToken);

    public Task<AdbCommandResult> KillServerAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(["kill-server"], cancellationToken);

    public async Task<AdbDevicesResult> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var commandResult = await ExecuteAsync(["devices", "-l"], cancellationToken)
            .ConfigureAwait(false);
        if (!commandResult.IsSuccess)
        {
            return new AdbDevicesResult(
                false,
                [],
                [],
                commandResult.ExitCode,
                commandResult.StandardError,
                commandResult.ErrorMessage);
        }

        var parseResult = _devicesParser.Parse(commandResult.StandardOutput);
        if (!parseResult.IsValid)
        {
            return new AdbDevicesResult(
                false,
                [],
                parseResult.Warnings,
                commandResult.ExitCode,
                commandResult.StandardError,
                parseResult.ErrorMessage);
        }

        var devices = new List<AndroidDevice>(parseResult.Devices.Count);
        var warnings = new List<string>(parseResult.Warnings);

        foreach (var device in parseResult.Devices)
        {
            if (device.State != AndroidDeviceState.Device)
            {
                devices.Add(device);
                continue;
            }

            var deviceInfo = await GetDeviceInfoAsync(device, cancellationToken).ConfigureAwait(false);
            devices.Add(deviceInfo.Device!);
            warnings.AddRange(deviceInfo.Warnings);
        }

        return new AdbDevicesResult(
            true,
            devices,
            warnings,
            commandResult.ExitCode,
            commandResult.StandardError,
            null);
    }

    public Task<AdbDeviceInfoResult> GetDeviceInfoAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var device = new AndroidDevice(
            serial,
            AndroidDeviceState.Device,
            null,
            null,
            null,
            AdbDevicesParser.DetectConnectionType(serial));

        return GetDeviceInfoAsync(device, cancellationToken);
    }

    private async Task<AdbCommandResult> ExecuteAsync(
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        var resolution = _pathResolver.Resolve();
        if (!resolution.IsResolved)
        {
            return new AdbCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                resolution.ErrorMessage);
        }

        return await ExecuteResolvedAsync(
                resolution.ExecutablePath!,
                arguments,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AdbDeviceInfoResult> GetDeviceInfoAsync(
        AndroidDevice device,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var manufacturer = await GetPropertyAsync(
                device.Serial,
                "ro.product.manufacturer",
                warnings,
                cancellationToken)
            .ConfigureAwait(false);
        var model = await GetPropertyAsync(
                device.Serial,
                "ro.product.model",
                warnings,
                cancellationToken)
            .ConfigureAwait(false);
        var androidVersion = await GetPropertyAsync(
                device.Serial,
                "ro.build.version.release",
                warnings,
                cancellationToken)
            .ConfigureAwait(false);

        var enrichedDevice = device with
        {
            Manufacturer = manufacturer,
            Model = model ?? device.Model,
            AndroidVersion = androidVersion
        };

        return new AdbDeviceInfoResult(true, enrichedDevice, warnings, null);
    }

    private async Task<string?> GetPropertyAsync(
        string serial,
        string propertyName,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(
                ["-s", serial, "shell", "getprop", propertyName],
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            warnings.Add(
                $"Could not read Android property '{propertyName}' for '{serial}': {result.ErrorMessage}");
            return null;
        }

        var value = result.StandardOutput.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private async Task<AdbCommandResult> ExecuteResolvedAsync(
        string executablePath,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var processResult = await _processRunner.RunAsync(
                    executablePath,
                    arguments,
                    DefaultCommandTimeout,
                    cancellationToken)
                .ConfigureAwait(false);

            var isSuccess = processResult.ExitCode == 0;
            return new AdbCommandResult(
                isSuccess,
                processResult.ExitCode,
                processResult.StandardOutput,
                processResult.StandardError,
                isSuccess ? null : CreateProcessFailureMessage(processResult));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new AdbCommandResult(
                false,
                null,
                string.Empty,
                string.Empty,
                $"ADB could not be executed: {exception.Message}");
        }
    }

    private static string CreateProcessFailureMessage(ProcessResult processResult)
    {
        var details = string.IsNullOrWhiteSpace(processResult.StandardError)
            ? "No error output was produced."
            : processResult.StandardError.Trim();

        return $"ADB exited with code {processResult.ExitCode}: {details}";
    }
}
