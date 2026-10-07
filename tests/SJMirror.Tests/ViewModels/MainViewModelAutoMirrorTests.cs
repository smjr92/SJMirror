using SJMirror.App.Models;
using SJMirror.App.Services;
using SJMirror.App.ViewModels;

namespace SJMirror.Tests.ViewModels;

public sealed class MainViewModelAutoMirrorTests
{
    [Fact]
    public async Task AutoMirrorDisabledDoesNotStartSession()
    {
        var context = CreateContext(new MirrorSettings { AutoMirror = false }, Device());
        await using var viewModel = context.ViewModel;

        await viewModel.InitializeAsync();

        Assert.Empty(context.MirrorService.StartedSessions);
    }

    [Fact]
    public async Task AutoMirrorStartsForOneNewReadyDevice()
    {
        var context = CreateContext(new MirrorSettings { AutoMirror = true }, Device());
        await using var viewModel = context.ViewModel;

        await viewModel.InitializeAsync();

        var session = Assert.Single(context.MirrorService.StartedSessions);
        Assert.Equal("ABC123", session.Device.Serial);
    }

    [Theory]
    [InlineData(AndroidDeviceState.Unauthorized)]
    [InlineData(AndroidDeviceState.Offline)]
    [InlineData(AndroidDeviceState.Unknown)]
    public async Task AutoMirrorDoesNotStartForUnavailableState(AndroidDeviceState state)
    {
        var context = CreateContext(
            new MirrorSettings { AutoMirror = true },
            Device(state: state));
        await using var viewModel = context.ViewModel;

        await viewModel.InitializeAsync();

        Assert.Empty(context.MirrorService.StartedSessions);
    }

    [Fact]
    public async Task RepeatedRefreshDoesNotStartDuplicateSession()
    {
        var context = CreateContext(new MirrorSettings { AutoMirror = true }, Device());
        await using var viewModel = context.ViewModel;
        await viewModel.InitializeAsync();

        await viewModel.RefreshAsync();

        Assert.Single(context.MirrorService.StartedSessions);
    }

    [Fact]
    public async Task StopDoesNotImmediatelyRestartAutoMirror()
    {
        var context = CreateContext(new MirrorSettings { AutoMirror = true }, Device());
        await using var viewModel = context.ViewModel;
        await viewModel.InitializeAsync();

        await viewModel.StopMirrorAsync();
        await viewModel.RefreshAsync();

        Assert.Single(context.MirrorService.StartedSessions);
    }

    [Fact]
    public async Task RemoveAndReconnectAllowsNewAutoMirrorSession()
    {
        var context = CreateContext(new MirrorSettings { AutoMirror = true }, Device());
        await using var viewModel = context.ViewModel;
        await viewModel.InitializeAsync();
        await viewModel.StopMirrorAsync();

        context.AdbService.Devices = [];
        await viewModel.RefreshAsync();
        context.AdbService.Devices = [Device()];
        await viewModel.RefreshAsync();

        Assert.Equal(2, context.MirrorService.StartedSessions.Count);
    }

    [Fact]
    public async Task SettingsChangesDoNotAlterActiveSessionSnapshot()
    {
        var context = CreateContext(new MirrorSettings(), Device());
        await using var viewModel = context.ViewModel;
        await viewModel.InitializeAsync();
        await viewModel.StartMirrorAsync();
        var activeSettings = Assert.Single(context.MirrorService.StartedSessions).Settings;

        viewModel.QualityProfile = MirrorQualityProfile.Quality;
        viewModel.TurnScreenOff = true;

        Assert.Equal(MirrorQualityProfile.Balanced, activeSettings.QualityProfile);
        Assert.False(activeSettings.TurnScreenOff);
        Assert.Equal(
            "Changes will apply to the next mirror session.",
            viewModel.SettingsChangeMessage);
    }

    private static TestContext CreateContext(MirrorSettings settings, AndroidDevice device)
    {
        var adbService = new FakeAdbService { Devices = [device] };
        var mirrorService = new FakeMirrorService();
        var viewModel = new MainViewModel(
            adbService,
            mirrorService,
            new FakeWirelessAdbService(),
            new FakeSettingsStore(settings));
        return new TestContext(viewModel, adbService, mirrorService);
    }

    private static AndroidDevice Device(
        string serial = "ABC123",
        AndroidDeviceState state = AndroidDeviceState.Device) =>
        new(serial, state, "Manufacturer", "Model", "16", AndroidConnectionType.Usb);

    private sealed record TestContext(
        MainViewModel ViewModel,
        FakeAdbService AdbService,
        FakeMirrorService MirrorService);

    private sealed class FakeAdbService : IAdbService
    {
        public IReadOnlyList<AndroidDevice> Devices { get; set; } = [];

        public Task<AdbAvailabilityResult> CheckAvailabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdbAvailabilityResult(true, @"C:\test\adb.exe", null));

        public Task<AdbDevicesResult> GetDevicesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdbDevicesResult(true, Devices, [], 0, string.Empty, null));

        public Task<AdbCommandResult> GetVersionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdbCommandResult> StartServerAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdbCommandResult> KillServerAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdbDeviceInfoResult> GetDeviceInfoAsync(
            string serial,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeMirrorService : IMirrorService
    {
        public event EventHandler<MirrorSessionEndedEventArgs>? SessionEnded;

        public bool IsRunning { get; private set; }

        public string? ActiveDeviceSerial { get; private set; }

        public List<StartedSession> StartedSessions { get; } = [];

        public Task<MirrorOperationResult> StartAsync(
            AndroidDevice device,
            CancellationToken cancellationToken = default) =>
            StartAsync(device, new MirrorSettings(), cancellationToken);

        public Task<MirrorOperationResult> StartAsync(
            AndroidDevice device,
            MirrorSettings settings,
            CancellationToken cancellationToken = default)
        {
            StartedSessions.Add(new StartedSession(device, settings));
            IsRunning = true;
            ActiveDeviceSerial = device.Serial;
            return Task.FromResult(MirrorOperationResult.Success);
        }

        public Task<MirrorOperationResult> StopAsync(CancellationToken cancellationToken = default)
        {
            var serial = ActiveDeviceSerial ?? string.Empty;
            IsRunning = false;
            ActiveDeviceSerial = null;
            SessionEnded?.Invoke(
                this,
                new MirrorSessionEndedEventArgs(serial, 0, string.Empty, string.Empty, true, null));
            return Task.FromResult(MirrorOperationResult.Success);
        }

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record StartedSession(AndroidDevice Device, MirrorSettings Settings);

    private sealed class FakeSettingsStore(MirrorSettings settings) : IMirrorSettingsStore
    {
        public Task<MirrorSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(
            MirrorSettings value,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeWirelessAdbService : IWirelessAdbService
    {
        public Task ReconnectKnownDevicesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

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
