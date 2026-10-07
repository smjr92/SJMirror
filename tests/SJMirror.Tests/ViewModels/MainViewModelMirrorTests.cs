using SJMirror.App.Models;
using SJMirror.App.Services;
using SJMirror.App.ViewModels;

namespace SJMirror.Tests.ViewModels;

public sealed class MainViewModelMirrorTests
{
    [Fact]
    public async Task MirrorCommandStartsSelectedDevice()
    {
        var mirrorService = new FakeMirrorService();
        await using var viewModel = CreateViewModel(mirrorService);
        var first = Device("FIRST");
        var second = Device("SECOND");
        viewModel.Devices.Add(first);
        viewModel.Devices.Add(second);
        viewModel.SelectedDevice = second;

        await viewModel.MirrorCommand.ExecuteAsync();

        Assert.Equal("SECOND", mirrorService.StartedDevice?.Serial);
        Assert.Equal(MirrorSessionState.Mirroring, viewModel.MirrorState);
        Assert.Equal("Mirroring", viewModel.SelectedStateTitle);
        Assert.Equal("Mouse & keyboard control enabled.", viewModel.MirrorMessage);
    }

    [Fact]
    public async Task SelectingReadyDeviceNotifiesThatMirrorCanStart()
    {
        var mirrorService = new FakeMirrorService();
        await using var viewModel = CreateViewModel(mirrorService);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        viewModel.SelectedDevice = Device("ABC123");

        Assert.True(viewModel.CanStartMirror);
        Assert.Contains(nameof(MainViewModel.CanStartMirror), changedProperties);
    }

    [Theory]
    [InlineData(AndroidDeviceState.Unauthorized)]
    [InlineData(AndroidDeviceState.Offline)]
    public async Task MirrorCommandIsDisabledForUnavailableDevice(AndroidDeviceState state)
    {
        var mirrorService = new FakeMirrorService();
        await using var viewModel = CreateViewModel(mirrorService);
        viewModel.SelectedDevice = Device("ABC123", state);

        Assert.False(viewModel.CanStartMirror);
        await viewModel.MirrorCommand.ExecuteAsync();
        Assert.Null(mirrorService.StartedDevice);
    }

    [Fact]
    public async Task StopCommandStopsSessionAndReturnsToReady()
    {
        var mirrorService = new FakeMirrorService();
        await using var viewModel = CreateViewModel(mirrorService);
        viewModel.SelectedDevice = Device("ABC123");
        await viewModel.MirrorCommand.ExecuteAsync();

        await viewModel.StopMirrorCommand.ExecuteAsync();

        Assert.True(mirrorService.StopCalled);
        Assert.Equal(MirrorSessionState.Ready, viewModel.MirrorState);
    }

    [Fact]
    public async Task NewSessionCanStartAfterStop()
    {
        var mirrorService = new FakeMirrorService();
        await using var viewModel = CreateViewModel(mirrorService);
        viewModel.SelectedDevice = Device("FIRST");
        await viewModel.StartMirrorAsync();
        await viewModel.StopMirrorAsync();

        viewModel.SelectedDevice = Device("SECOND");
        await viewModel.StartMirrorAsync();

        Assert.Equal(["FIRST", "SECOND"], mirrorService.StartedSerials);
        Assert.Equal("SECOND", viewModel.MirroredDeviceSerial);
    }

    [Fact]
    public async Task ChangingSelectionDoesNotChangeMirroredDevice()
    {
        var mirrorService = new FakeMirrorService();
        await using var viewModel = CreateViewModel(mirrorService);
        viewModel.SelectedDevice = Device("FIRST");
        await viewModel.StartMirrorAsync();

        viewModel.SelectedDevice = Device("SECOND");

        Assert.Equal("SECOND", viewModel.SelectedDevice.Serial);
        Assert.Equal("FIRST", viewModel.MirroredDeviceSerial);
        Assert.Equal("FIRST", mirrorService.ActiveDeviceSerial);
    }

    [Fact]
    public async Task ManualWindowCloseReturnsSessionToReady()
    {
        var mirrorService = new FakeMirrorService();
        await using var viewModel = CreateViewModel(mirrorService);
        viewModel.SelectedDevice = Device("ABC123");
        await viewModel.StartMirrorAsync();

        mirrorService.EndNormally();

        Assert.Equal(MirrorSessionState.Ready, viewModel.MirrorState);
        Assert.Null(viewModel.MirroredDeviceSerial);
        Assert.Equal("Screen mirroring window was closed.", viewModel.MirrorMessage);
    }

    [Fact]
    public async Task DuplicateStartDoesNotCreateSecondSession()
    {
        var mirrorService = new FakeMirrorService();
        await using var viewModel = CreateViewModel(mirrorService);
        viewModel.SelectedDevice = Device("ABC123");

        await viewModel.StartMirrorAsync();
        await viewModel.StartMirrorAsync();

        Assert.Single(mirrorService.StartedSerials);
    }

    [Fact]
    public async Task MirroredDeviceGoingOfflineKeepsSessionUntilScrcpyEnds()
    {
        var online = Device("ABC123");
        var offline = Device("ABC123", AndroidDeviceState.Offline);
        var adbService = new MutableAdbService(online);
        var mirrorService = new FakeMirrorService();
        await using var viewModel = new MainViewModel(adbService, mirrorService);
        await viewModel.RefreshAsync();
        await viewModel.StartMirrorAsync();

        adbService.Device = offline;
        await viewModel.RefreshAsync();

        Assert.Equal(AndroidDeviceState.Offline, viewModel.SelectedDevice?.State);
        Assert.Equal("ABC123", viewModel.MirroredDeviceSerial);
        Assert.Equal(MirrorSessionState.Mirroring, viewModel.MirrorState);
        Assert.False(viewModel.CanStartMirror);

        mirrorService.EndNormally();
        adbService.Device = online;
        await viewModel.RefreshAsync();

        Assert.Equal(MirrorSessionState.Ready, viewModel.MirrorState);
        Assert.True(viewModel.CanStartMirror);
    }

    [Fact]
    public async Task UnexpectedProcessErrorUpdatesFriendlyUiState()
    {
        var mirrorService = new FakeMirrorService();
        await using var viewModel = CreateViewModel(mirrorService);
        viewModel.SelectedDevice = Device("ABC123");
        await viewModel.MirrorCommand.ExecuteAsync();

        mirrorService.EndWithError("decoder failed");

        Assert.Equal(MirrorSessionState.Error, viewModel.MirrorState);
        Assert.Equal("Screen mirroring ended unexpectedly.", viewModel.MirrorMessage);
        Assert.Equal("decoder failed", viewModel.DiagnosticMessage);
    }

    [Fact]
    public async Task InputInjectionPermissionErrorShowsManufacturerGuidance()
    {
        var mirrorService = new FakeMirrorService();
        await using var viewModel = CreateViewModel(mirrorService);
        viewModel.SelectedDevice = Device("ABC123");
        await viewModel.MirrorCommand.ExecuteAsync();

        mirrorService.EndWithError(
            "java.lang.SecurityException: Injecting input events requires permission");

        Assert.Equal(MirrorSessionState.Error, viewModel.MirrorState);
        Assert.Contains("debugging/security option", viewModel.MirrorMessage);
    }

    private static MainViewModel CreateViewModel(IMirrorService mirrorService) =>
        new(new UnusedAdbService(), mirrorService);

    private static AndroidDevice Device(
        string serial,
        AndroidDeviceState state = AndroidDeviceState.Device) =>
        new(serial, state, "Manufacturer", "Model", "16", AndroidConnectionType.Usb);

    private sealed class FakeMirrorService : IMirrorService
    {
        public event EventHandler<MirrorSessionEndedEventArgs>? SessionEnded;

        public bool IsRunning { get; private set; }

        public string? ActiveDeviceSerial { get; private set; }

        public AndroidDevice? StartedDevice { get; private set; }

        public List<string> StartedSerials { get; } = [];

        public bool StopCalled { get; private set; }

        public Task<MirrorOperationResult> StartAsync(
            AndroidDevice device,
            CancellationToken cancellationToken = default)
        {
            StartedDevice = device;
            StartedSerials.Add(device.Serial);
            ActiveDeviceSerial = device.Serial;
            IsRunning = true;
            return Task.FromResult(MirrorOperationResult.Success);
        }

        public Task<MirrorOperationResult> StopAsync(
            CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            IsRunning = false;
            var serial = ActiveDeviceSerial ?? string.Empty;
            ActiveDeviceSerial = null;
            SessionEnded?.Invoke(
                this,
                new MirrorSessionEndedEventArgs(
                    serial,
                    0,
                    string.Empty,
                    string.Empty,
                    true,
                    null));
            return Task.FromResult(MirrorOperationResult.Success);
        }

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            ActiveDeviceSerial = null;
            return ValueTask.CompletedTask;
        }

        public void EndWithError(string error)
        {
            var serial = ActiveDeviceSerial ?? string.Empty;
            IsRunning = false;
            ActiveDeviceSerial = null;
            SessionEnded?.Invoke(
                this,
                new MirrorSessionEndedEventArgs(
                    serial,
                    1,
                    string.Empty,
                    error,
                    false,
                    error));
        }

        public void EndNormally()
        {
            var serial = ActiveDeviceSerial ?? string.Empty;
            IsRunning = false;
            ActiveDeviceSerial = null;
            SessionEnded?.Invoke(
                this,
                new MirrorSessionEndedEventArgs(
                    serial,
                    0,
                    string.Empty,
                    string.Empty,
                    false,
                    null));
        }
    }

    private sealed class UnusedAdbService : IAdbService
    {
        public Task<AdbAvailabilityResult> CheckAvailabilityAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdbCommandResult> GetVersionAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdbCommandResult> StartServerAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdbCommandResult> KillServerAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdbDevicesResult> GetDevicesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdbDeviceInfoResult> GetDeviceInfoAsync(
            string serial,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MutableAdbService(AndroidDevice device) : IAdbService
    {
        public AndroidDevice Device { get; set; } = device;

        public Task<AdbAvailabilityResult> CheckAvailabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdbAvailabilityResult(true, @"C:\test-tools\adb.exe", null));

        public Task<AdbDevicesResult> GetDevicesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdbDevicesResult(true, [Device], [], 0, string.Empty, null));

        public Task<AdbCommandResult> GetVersionAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdbCommandResult> StartServerAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdbCommandResult> KillServerAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdbDeviceInfoResult> GetDeviceInfoAsync(
            string serial,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
