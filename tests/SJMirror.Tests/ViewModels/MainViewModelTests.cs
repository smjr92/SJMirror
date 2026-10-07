using SJMirror.App.Models;
using SJMirror.App.Services;
using SJMirror.App.ViewModels;

namespace SJMirror.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task RefreshShowsNoDeviceState()
    {
        var adbService = new FakeAdbService { DevicesResult = DevicesResult([]) };
        await using var viewModel = CreateViewModel(adbService);

        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.Devices);
        Assert.Null(viewModel.SelectedDevice);
        Assert.Equal("ADB Ready", viewModel.AdbStatus);
        Assert.Contains("No Android device", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RefreshShowsConnectedDeviceDetails()
    {
        var device = Device("ONE", AndroidDeviceState.Device, "Xiaomi", "2511FPC34G", "16");
        var adbService = new FakeAdbService { DevicesResult = DevicesResult([device]) };
        await using var viewModel = CreateViewModel(adbService);

        await viewModel.RefreshAsync();

        Assert.Same(device, viewModel.SelectedDevice);
        Assert.Equal("Xiaomi", viewModel.SelectedDeviceManufacturer);
        Assert.Equal("Android 16", viewModel.SelectedAndroidVersion);
        Assert.Equal("Connected", viewModel.SelectedStateTitle);
    }

    [Fact]
    public async Task RefreshShowsMultipleDevicesAndSelectsFirstPredictably()
    {
        var first = Device("ONE");
        var second = Device("TWO");
        var adbService = new FakeAdbService { DevicesResult = DevicesResult([first, second]) };
        await using var viewModel = CreateViewModel(adbService);

        await viewModel.RefreshAsync();

        Assert.Equal(2, viewModel.Devices.Count);
        Assert.Same(first, viewModel.SelectedDevice);
        Assert.Equal("2 devices detected", viewModel.StatusMessage);
    }

    [Fact]
    public async Task UnauthorizedDeviceShowsAuthorizationInstructions()
    {
        var adbService = new FakeAdbService
        {
            DevicesResult = DevicesResult([Device("ONE", AndroidDeviceState.Unauthorized)])
        };
        await using var viewModel = CreateViewModel(adbService);

        await viewModel.RefreshAsync();

        Assert.Equal("Authorization required", viewModel.SelectedStateTitle);
        Assert.Contains("accept the USB debugging authorization", viewModel.SelectedStateDescription);
    }

    [Fact]
    public async Task OfflineDeviceShowsOfflineState()
    {
        var adbService = new FakeAdbService
        {
            DevicesResult = DevicesResult([Device("ONE", AndroidDeviceState.Offline)])
        };
        await using var viewModel = CreateViewModel(adbService);

        await viewModel.RefreshAsync();

        Assert.Equal("Device offline", viewModel.SelectedStateTitle);
    }

    [Theory]
    [InlineData(AndroidDeviceState.Unauthorized, AndroidDeviceState.Device)]
    [InlineData(AndroidDeviceState.Device, AndroidDeviceState.Offline)]
    [InlineData(AndroidDeviceState.Offline, AndroidDeviceState.Device)]
    public async Task RefreshUpdatesExistingDeviceState(
        AndroidDeviceState initialState,
        AndroidDeviceState updatedState)
    {
        var results = new Queue<AdbDevicesResult>(
        [
            DevicesResult([Device("ONE", initialState)]),
            DevicesResult([Device("ONE", updatedState)])
        ]);
        var adbService = new FakeAdbService
        {
            GetDevicesHandler = _ => Task.FromResult(results.Dequeue())
        };
        await using var viewModel = CreateViewModel(adbService);

        await viewModel.RefreshAsync();
        await viewModel.RefreshAsync();

        Assert.Equal(updatedState, Assert.Single(viewModel.Devices).State);
        Assert.Equal(updatedState, viewModel.SelectedDevice?.State);
    }

    [Fact]
    public async Task SelectedDeviceCanBeChanged()
    {
        var first = Device("ONE");
        var second = Device("TWO", model: "Second phone");
        var adbService = new FakeAdbService { DevicesResult = DevicesResult([first, second]) };
        await using var viewModel = CreateViewModel(adbService);
        await viewModel.RefreshAsync();

        viewModel.SelectedDevice = second;

        Assert.Same(second, viewModel.SelectedDevice);
        Assert.Equal("Second phone", viewModel.SelectedDeviceName);
    }

    [Fact]
    public async Task RefreshCommandRequestsNewDeviceList()
    {
        var adbService = new FakeAdbService { DevicesResult = DevicesResult([]) };
        await using var viewModel = CreateViewModel(adbService);

        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.Equal(1, adbService.GetDevicesCallCount);
    }

    [Fact]
    public async Task InitializeRequestsWirelessReconnectBeforeLoadingDevices()
    {
        var wirelessService = new FakeWirelessAdbService();
        var adbService = new FakeAdbService
        {
            GetDevicesHandler = _ =>
            {
                Assert.True(wirelessService.ReconnectCalled);
                return Task.FromResult(DevicesResult([]));
            }
        };
        await using var viewModel = new MainViewModel(
            adbService,
            new NoOpMirrorService(),
            wirelessService);

        await viewModel.InitializeAsync();

        Assert.True(wirelessService.ReconnectCalled);
        Assert.Equal(1, adbService.GetDevicesCallCount);
    }

    [Fact]
    public async Task MissingAdbShowsFriendlyFailure()
    {
        var adbService = new FakeAdbService
        {
            AvailabilityResult = new AdbAvailabilityResult(false, null, "technical details")
        };
        await using var viewModel = CreateViewModel(adbService);

        await viewModel.RefreshAsync();

        Assert.Equal("ADB not found", viewModel.AdbStatus);
        Assert.Contains("Android Platform Tools", viewModel.StatusMessage);
        Assert.Equal(0, adbService.GetDevicesCallCount);
    }

    [Fact]
    public async Task RemovedSelectedDeviceFallsBackToRemainingDevice()
    {
        var first = Device("ONE");
        var second = Device("TWO");
        var results = new Queue<AdbDevicesResult>(
        [
            DevicesResult([first, second]),
            DevicesResult([second])
        ]);
        var adbService = new FakeAdbService
        {
            GetDevicesHandler = _ => Task.FromResult(results.Dequeue())
        };
        await using var viewModel = CreateViewModel(adbService);
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = first;

        await viewModel.RefreshAsync();

        Assert.Single(viewModel.Devices);
        Assert.Same(second, viewModel.SelectedDevice);
    }

    [Fact]
    public async Task ConcurrentRefreshIsSkipped()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource<AdbDevicesResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var adbService = new FakeAdbService
        {
            GetDevicesHandler = _ =>
            {
                refreshStarted.TrySetResult();
                return releaseRefresh.Task;
            }
        };
        await using var viewModel = CreateViewModel(adbService);

        var firstRefresh = viewModel.RefreshAsync();
        await refreshStarted.Task;
        await viewModel.RefreshAsync();

        Assert.Equal(1, adbService.GetDevicesCallCount);
        releaseRefresh.SetResult(DevicesResult([]));
        await firstRefresh;
    }

    [Fact]
    public async Task DisposeCancelsPendingManualRefresh()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adbService = new FakeAdbService
        {
            GetDevicesHandler = cancellationToken =>
            {
                refreshStarted.TrySetResult();
                return WaitForCancellationAsync(cancellationToken);
            }
        };
        var viewModel = new MainViewModel(adbService);

        var refreshTask = viewModel.RefreshAsync();
        await refreshStarted.Task;

        await viewModel.DisposeAsync();
        await refreshTask;

        Assert.True(adbService.LastDevicesCancellationToken.IsCancellationRequested);
    }

    private static MainViewModel CreateViewModel(IAdbService adbService) =>
        new(adbService);

    private static AndroidDevice Device(
        string serial,
        AndroidDeviceState state = AndroidDeviceState.Device,
        string? manufacturer = "Manufacturer",
        string? model = "Model",
        string? androidVersion = "16") =>
        new(serial, state, manufacturer, model, androidVersion, AndroidConnectionType.Usb);

    private static AdbDevicesResult DevicesResult(IReadOnlyList<AndroidDevice> devices) =>
        new(true, devices, [], 0, string.Empty, null);

    private static async Task<AdbDevicesResult> WaitForCancellationAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return DevicesResult([]);
    }

    private sealed class FakeAdbService : IAdbService
    {
        public AdbAvailabilityResult AvailabilityResult { get; set; } =
            new(true, @"C:\test-tools\adb.exe", null);

        public AdbDevicesResult DevicesResult { get; set; } = MainViewModelTests.DevicesResult([]);

        public Func<CancellationToken, Task<AdbDevicesResult>>? GetDevicesHandler { get; set; }

        public int GetDevicesCallCount { get; private set; }

        public CancellationToken LastDevicesCancellationToken { get; private set; }

        public Task<AdbAvailabilityResult> CheckAvailabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AvailabilityResult);

        public Task<AdbDevicesResult> GetDevicesAsync(
            CancellationToken cancellationToken = default)
        {
            GetDevicesCallCount++;
            LastDevicesCancellationToken = cancellationToken;
            return GetDevicesHandler?.Invoke(cancellationToken) ?? Task.FromResult(DevicesResult);
        }

        public Task<AdbCommandResult> GetVersionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdbCommandResult> StartServerAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdbCommandResult> KillServerAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdbDeviceInfoResult> GetDeviceInfoAsync(
            string serial,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoOpMirrorService : IMirrorService
    {
        public event EventHandler<MirrorSessionEndedEventArgs>? SessionEnded
        {
            add { }
            remove { }
        }

        public bool IsRunning => false;

        public string? ActiveDeviceSerial => null;

        public Task<MirrorOperationResult> StartAsync(
            AndroidDevice device,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(MirrorOperationResult.Success);

        public Task<MirrorOperationResult> StopAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(MirrorOperationResult.Success);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeWirelessAdbService : IWirelessAdbService
    {
        public bool ReconnectCalled { get; private set; }

        public Task ReconnectKnownDevicesAsync(CancellationToken cancellationToken = default)
        {
            ReconnectCalled = true;
            return Task.CompletedTask;
        }

        public Task<WirelessPairingSession> CreateQrPairingSessionAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<WirelessPairingResult> WaitForQrPairingAsync(
            WirelessPairingSession session,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<WirelessPairingResult> PairWithCodeAsync(
            string ipAddress,
            int port,
            string pairingCode,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<WirelessMdnsService>> GetMdnsServicesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Cancel(WirelessPairingSession session) => throw new NotSupportedException();
    }

}
