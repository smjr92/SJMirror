using SJMirror.App.Models;
using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class ScrcpyMirrorServiceTests
{
    private const string ScrcpyPath = @"C:\test-tools\scrcpy.exe";

    [Fact]
    public async Task StartAsyncReturnsControlledFailureWhenScrcpyIsMissing()
    {
        var launcher = new FakeManagedProcessLauncher();
        await using var service = new ScrcpyMirrorService(
            new FakeScrcpyPathResolver(new ScrcpyPathResolution(null, "scrcpy not found")),
            launcher);

        var result = await service.StartAsync(Device("ABC123"));

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.ErrorMessage);
        Assert.Empty(launcher.Invocations);
    }

    [Fact]
    public async Task StartAsyncStartsSessionForValidDeviceWithSeparateSerialArgument()
    {
        var launcher = new FakeManagedProcessLauncher();
        await using var service = CreateService(launcher);

        var result = await service.StartAsync(Device("ABC123"));

        Assert.True(result.IsSuccess);
        Assert.True(service.IsRunning);
        Assert.Equal("ABC123", service.ActiveDeviceSerial);
        var invocation = Assert.Single(launcher.Invocations);
        Assert.Equal(ScrcpyPath, invocation.ExecutablePath);
        Assert.Equal(["--serial", "ABC123", "--mouse=sdk"], invocation.Arguments.Take(3));
        Assert.Contains("--max-size=1920", invocation.Arguments);
        Assert.Contains("--stay-awake", invocation.Arguments);
        Assert.DoesNotContain("--no-control", invocation.Arguments);
        Assert.DoesNotContain("--mouse=disabled", invocation.Arguments);
    }

    [Fact]
    public async Task StartAsyncUsesExactWirelessAdbSerial()
    {
        var launcher = new FakeManagedProcessLauncher();
        await using var service = CreateService(launcher);

        var result = await service.StartAsync(Device(
            "192.168.1.40:39877",
            connectionType: AndroidConnectionType.Network));

        Assert.True(result.IsSuccess);
        var invocation = Assert.Single(launcher.Invocations);
        Assert.Equal(
            ["--serial", "192.168.1.40:39877", "--mouse=sdk"],
            invocation.Arguments.Take(3));
    }

    [Theory]
    [InlineData(AndroidDeviceState.Unauthorized)]
    [InlineData(AndroidDeviceState.Offline)]
    public async Task StartAsyncRejectsUnavailableDeviceStates(AndroidDeviceState state)
    {
        var launcher = new FakeManagedProcessLauncher();
        await using var service = CreateService(launcher);

        var result = await service.StartAsync(Device("ABC123", state));

        Assert.False(result.IsSuccess);
        Assert.False(service.IsRunning);
        Assert.Empty(launcher.Invocations);
    }

    [Fact]
    public async Task StartAsyncRejectsSecondSession()
    {
        var launcher = new FakeManagedProcessLauncher();
        await using var service = CreateService(launcher);
        await service.StartAsync(Device("FIRST"));

        var result = await service.StartAsync(Device("SECOND"));

        Assert.False(result.IsSuccess);
        Assert.Equal("FIRST", service.ActiveDeviceSerial);
        Assert.Single(launcher.Invocations);
    }

    [Fact]
    public async Task StopAsyncStopsOnlyManagedProcess()
    {
        var process = new FakeManagedProcess();
        var launcher = new FakeManagedProcessLauncher(process);
        await using var service = CreateService(launcher);
        await service.StartAsync(Device("ABC123"));

        var result = await service.StopAsync();

        Assert.True(result.IsSuccess);
        Assert.True(process.StopCalled);
        Assert.False(service.IsRunning);
        Assert.Null(service.ActiveDeviceSerial);
    }

    [Fact]
    public async Task ProcessClosedByUserEndsSessionWithoutError()
    {
        var process = new FakeManagedProcess();
        var launcher = new FakeManagedProcessLauncher(process);
        await using var service = CreateService(launcher);
        var sessionEnded = WaitForSessionEnded(service);
        await service.StartAsync(Device("ABC123"));

        process.Complete(new ManagedProcessResult(0, "normal output", string.Empty));
        var eventArgs = await sessionEnded.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(eventArgs.WasStopped);
        Assert.Null(eventArgs.ErrorMessage);
        Assert.Equal("normal output", eventArgs.StandardOutput);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task ProcessErrorReturnsExitCodeAndStandardError()
    {
        var process = new FakeManagedProcess();
        var launcher = new FakeManagedProcessLauncher(process);
        await using var service = CreateService(launcher);
        var sessionEnded = WaitForSessionEnded(service);
        await service.StartAsync(Device("ABC123"));

        process.Complete(new ManagedProcessResult(3, string.Empty, "device disconnected"));
        var eventArgs = await sessionEnded.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, eventArgs.ExitCode);
        Assert.Equal("device disconnected", eventArgs.StandardError);
        Assert.Equal("device disconnected", eventArgs.ErrorMessage);
    }

    [Fact]
    public async Task StartAsyncHonorsCancellation()
    {
        var launcher = new FakeManagedProcessLauncher();
        await using var service = CreateService(launcher);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.StartAsync(Device("ABC123"), cancellationSource.Token));

        Assert.Empty(launcher.Invocations);
    }

    [Fact]
    public async Task DisposeStopsActiveSession()
    {
        var process = new FakeManagedProcess();
        var launcher = new FakeManagedProcessLauncher(process);
        var service = CreateService(launcher);
        await service.StartAsync(Device("ABC123"));

        await service.DisposeAsync();

        Assert.True(process.StopCalled);
        Assert.True(process.DisposeCalled);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task ConcurrentStopCallsStopManagedProcessOnlyOnce()
    {
        var process = new FakeManagedProcess();
        var launcher = new FakeManagedProcessLauncher(process);
        await using var service = CreateService(launcher);
        await service.StartAsync(Device("ABC123"));

        await Task.WhenAll(service.StopAsync(), service.StopAsync());

        Assert.Equal(1, process.StopCallCount);
    }

    private static ScrcpyMirrorService CreateService(FakeManagedProcessLauncher launcher) =>
        new(
            new FakeScrcpyPathResolver(new ScrcpyPathResolution(ScrcpyPath, null)),
            launcher);

    private static AndroidDevice Device(
        string serial,
        AndroidDeviceState state = AndroidDeviceState.Device,
        AndroidConnectionType connectionType = AndroidConnectionType.Usb) =>
        new(serial, state, "Manufacturer", "Model", "16", connectionType);

    private static Task<MirrorSessionEndedEventArgs> WaitForSessionEnded(IMirrorService service)
    {
        var completion = new TaskCompletionSource<MirrorSessionEndedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.SessionEnded += (_, eventArgs) => completion.TrySetResult(eventArgs);
        return completion.Task;
    }

    private sealed class FakeScrcpyPathResolver(ScrcpyPathResolution resolution)
        : IScrcpyPathResolver
    {
        public ScrcpyPathResolution Resolve() => resolution;
    }

    private sealed class FakeManagedProcessLauncher : IManagedProcessLauncher
    {
        private readonly IManagedProcess _process;

        public FakeManagedProcessLauncher(IManagedProcess? process = null)
        {
            _process = process ?? new FakeManagedProcess();
        }

        public List<ProcessInvocation> Invocations { get; } = [];

        public IManagedProcess Start(string executablePath, IEnumerable<string> arguments)
        {
            Invocations.Add(new ProcessInvocation(executablePath, arguments.ToArray()));
            return _process;
        }
    }

    private sealed class FakeManagedProcess : IManagedProcess
    {
        private readonly TaskCompletionSource<ManagedProcessResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool StopCalled { get; private set; }

        public int StopCallCount { get; private set; }

        public bool DisposeCalled { get; private set; }

        public bool HasExited => _completion.Task.IsCompleted;

        public Task<ManagedProcessResult> Completion => _completion.Task;

        public async Task<ManagedProcessResult> StopAsync(
            CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            StopCallCount++;
            _completion.TrySetResult(new ManagedProcessResult(0, string.Empty, string.Empty));
            return await _completion.Task.WaitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            return ValueTask.CompletedTask;
        }

        public void Complete(ManagedProcessResult result) => _completion.TrySetResult(result);
    }

    private sealed record ProcessInvocation(
        string ExecutablePath,
        IReadOnlyList<string> Arguments);
}
