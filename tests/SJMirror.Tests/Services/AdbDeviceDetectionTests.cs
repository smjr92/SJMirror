using SJMirror.App.Models;
using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class AdbDeviceDetectionTests
{
    private const string AdbPath = @"C:\test-tools\adb.exe";
    private const string Serial = "R58M123ABC";

    [Theory]
    [InlineData("ro.product.manufacturer", "Samsung", "Manufacturer")]
    [InlineData("ro.product.model", "Galaxy S25", "Model")]
    [InlineData("ro.build.version.release", "16", "AndroidVersion")]
    public async Task GetDevicesAsyncReadsAndroidProperties(
        string targetProperty,
        string propertyValue,
        string expectedMember)
    {
        var processRunner = new RecordingProcessRunner(arguments =>
        {
            if (arguments.SequenceEqual(["devices", "-l"]))
            {
                return Success($"List of devices attached\n{Serial} device transport_id:1\n");
            }

            return Success(arguments[^1] == targetProperty ? propertyValue : string.Empty);
        });
        var service = CreateService(processRunner);

        var result = await service.GetDevicesAsync();

        var device = Assert.Single(result.Devices);
        Assert.True(result.IsSuccess);
        Assert.Equal(
            propertyValue,
            expectedMember switch
            {
                "Manufacturer" => device.Manufacturer,
                "Model" => device.Model,
                "AndroidVersion" => device.AndroidVersion,
                _ => throw new InvalidOperationException("Unexpected test member.")
            });
        Assert.Contains(
            processRunner.Invocations,
            invocation => invocation.Arguments.SequenceEqual(
                ["-s", Serial, "shell", "getprop", targetProperty]));
    }

    [Fact]
    public async Task GetDevicesAsyncKeepsDeviceWhenPropertyQueryFails()
    {
        var processRunner = new RecordingProcessRunner(arguments =>
        {
            if (arguments.SequenceEqual(["devices", "-l"]))
            {
                return Success(
                    $"List of devices attached\n{Serial} device model:Fallback_Model transport_id:1\n");
            }

            return arguments[^1] == "ro.product.model"
                ? new ProcessResult(1, string.Empty, "property unavailable")
                : Success("value");
        });
        var service = CreateService(processRunner);

        var result = await service.GetDevicesAsync();

        var device = Assert.Single(result.Devices);
        Assert.True(result.IsSuccess);
        Assert.Equal("Fallback Model", device.Model);
        Assert.Contains(result.Warnings, warning => warning.Contains("ro.product.model"));
    }

    [Fact]
    public async Task GetDevicesAsyncDoesNotQueryPropertiesForUnavailableDevices()
    {
        var processRunner = new RecordingProcessRunner(arguments =>
            arguments.SequenceEqual(["devices", "-l"])
                ? Success(
                    "List of devices attached\n" +
                    "UNAUTHORIZED unauthorized transport_id:1\n" +
                    "OFFLINE offline transport_id:2\n")
                : throw new InvalidOperationException("getprop must not be called."));
        var service = CreateService(processRunner);

        var result = await service.GetDevicesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Devices.Count);
        Assert.Equal(AndroidDeviceState.Unauthorized, result.Devices[0].State);
        Assert.Equal(AndroidDeviceState.Offline, result.Devices[1].State);
        Assert.Single(processRunner.Invocations);
    }

    [Fact]
    public async Task GetDevicesAsyncReturnsControlledFailureForUnexpectedOutput()
    {
        var processRunner = new RecordingProcessRunner(_ => Success("unexpected adb response"));
        var service = CreateService(processRunner);

        var result = await service.GetDevicesAsync();

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Devices);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task GetDevicesAsyncCanBeCalledRepeatedly()
    {
        var callCount = 0;
        var processRunner = new RecordingProcessRunner(arguments =>
        {
            if (arguments.SequenceEqual(["devices", "-l"]))
            {
                callCount++;
                return Success("List of devices attached\n");
            }

            throw new InvalidOperationException("Unexpected command.");
        });
        var service = CreateService(processRunner);

        await service.GetDevicesAsync();
        await service.GetDevicesAsync();

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetDevicesAsyncPropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var processRunner = new RecordingProcessRunner(
            (_, cancellationToken) => Task.FromCanceled<ProcessResult>(cancellationToken));
        var service = CreateService(processRunner);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetDevicesAsync(cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }

    private static AdbService CreateService(IProcessRunner processRunner) =>
        new(processRunner, new FixedAdbPathResolver());

    private static ProcessResult Success(string standardOutput) =>
        new(0, standardOutput, string.Empty);

    private sealed class FixedAdbPathResolver : IAdbPathResolver
    {
        public AdbPathResolution Resolve() => new(AdbPath, null);
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        private readonly Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> _handler;

        public RecordingProcessRunner(Func<IReadOnlyList<string>, ProcessResult> handler)
            : this((arguments, _) => Task.FromResult(handler(arguments)))
        {
        }

        public RecordingProcessRunner(
            Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> handler)
        {
            _handler = handler;
        }

        public List<ProcessInvocation> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            string executablePath,
            IEnumerable<string>? arguments = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var argumentList = arguments?.ToArray() ?? [];
            Invocations.Add(new ProcessInvocation(executablePath, argumentList));
            return _handler(argumentList, cancellationToken);
        }
    }

    private sealed record ProcessInvocation(
        string ExecutablePath,
        IReadOnlyList<string> Arguments);
}
