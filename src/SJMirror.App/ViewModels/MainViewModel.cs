using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SJMirror.App.Infrastructure;
using SJMirror.App.Models;
using SJMirror.App.Services;

namespace SJMirror.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IAdbService _adbService;
    private readonly IMirrorService _mirrorService;
    private readonly IWirelessAdbService _wirelessAdbService;
    private readonly IMirrorSettingsStore _settingsStore;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly CancellationTokenSource _lifetimeCancellationSource = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _mirrorOperationGate = new(1, 1);
    private readonly object _settingsSaveSync = new();
    private readonly HashSet<string> _knownDeviceSerials = new(StringComparer.Ordinal);

    private AndroidDevice? _selectedDevice;
    private bool _isLoading;
    private string _statusMessage = "Starting...";
    private string _adbStatus = "ADB checking";
    private string? _diagnosticMessage;
    private string? _mirrorMessage;
    private string? _mirrorTargetSerial;
    private MirrorSessionState _mirrorState = MirrorSessionState.Ready;
    private bool _isInitialized;
    private bool _isDisposed;
    private MirrorSettings _settings = new();
    private Task _settingsSaveTask = Task.CompletedTask;
    private string? _settingsChangeMessage;

    public MainViewModel()
        : this(
            new AdbService(),
            new ScrcpyMirrorService(),
            new WirelessAdbService(),
            new JsonMirrorSettingsStore())
    {
    }

    public MainViewModel(IAdbService adbService)
        : this(
            adbService,
            new ScrcpyMirrorService(),
            new WirelessAdbService(),
            new JsonMirrorSettingsStore())
    {
    }

    public MainViewModel(
        IAdbService adbService,
        IMirrorService mirrorService)
        : this(
            adbService,
            mirrorService,
            new WirelessAdbService(),
            new JsonMirrorSettingsStore())
    {
    }

    public MainViewModel(
        IAdbService adbService,
        IMirrorService mirrorService,
        IWirelessAdbService wirelessAdbService)
        : this(adbService, mirrorService, wirelessAdbService, new JsonMirrorSettingsStore())
    {
    }

    public MainViewModel(
        IAdbService adbService,
        IMirrorService mirrorService,
        IWirelessAdbService wirelessAdbService,
        IMirrorSettingsStore settingsStore)
    {
        _adbService = adbService ?? throw new ArgumentNullException(nameof(adbService));
        _mirrorService = mirrorService ?? throw new ArgumentNullException(nameof(mirrorService));
        _wirelessAdbService = wirelessAdbService ?? throw new ArgumentNullException(nameof(wirelessAdbService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _synchronizationContext = SynchronizationContext.Current;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        MirrorCommand = new AsyncRelayCommand(StartMirrorAsync);
        StopMirrorCommand = new AsyncRelayCommand(StopMirrorAsync, () => IsMirrorSessionActive);
        _mirrorService.SessionEnded += MirrorService_SessionEnded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ApplicationName => "SJ Mirror";

    public ObservableCollection<AndroidDevice> Devices { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand MirrorCommand { get; }

    public AsyncRelayCommand StopMirrorCommand { get; }

    public IReadOnlyList<MirrorQualityProfile> QualityProfiles { get; } =
        Enum.GetValues<MirrorQualityProfile>();

    public MirrorQualityProfile QualityProfile
    {
        get => _settings.QualityProfile;
        set
        {
            if (_settings.QualityProfile == value)
            {
                return;
            }

            _settings = _settings with { QualityProfile = value };
            OnPropertyChanged();
            SettingsChanged();
        }
    }

    public bool TurnScreenOff
    {
        get => _settings.TurnScreenOff;
        set
        {
            if (_settings.TurnScreenOff == value)
            {
                return;
            }

            _settings = _settings with { TurnScreenOff = value };
            OnPropertyChanged();
            SettingsChanged();
        }
    }

    public bool StayAwake
    {
        get => _settings.StayAwake;
        set
        {
            if (_settings.StayAwake == value)
            {
                return;
            }

            _settings = _settings with { StayAwake = value };
            OnPropertyChanged();
            SettingsChanged();
        }
    }

    public bool AutoMirror
    {
        get => _settings.AutoMirror;
        set
        {
            if (_settings.AutoMirror == value)
            {
                return;
            }

            _settings = _settings with { AutoMirror = value };
            OnPropertyChanged();
            SettingsChanged();
        }
    }

    public string? SettingsChangeMessage
    {
        get => _settingsChangeMessage;
        private set => SetProperty(ref _settingsChangeMessage, value);
    }

    public AndroidDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                NotifySelectedDeviceProperties();
                OnPropertyChanged(nameof(CanStartMirror));
                NotifyMirrorCommandState();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string AdbStatus
    {
        get => _adbStatus;
        private set => SetProperty(ref _adbStatus, value);
    }

    public string? DiagnosticMessage
    {
        get => _diagnosticMessage;
        private set => SetProperty(ref _diagnosticMessage, value);
    }

    public MirrorSessionState MirrorState
    {
        get => _mirrorState;
        private set
        {
            if (SetProperty(ref _mirrorState, value))
            {
                OnPropertyChanged(nameof(IsMirrorSessionActive));
                OnPropertyChanged(nameof(CanStartMirror));
                NotifySelectedDeviceProperties();
                NotifyMirrorCommandState();
            }
        }
    }

    public string? MirrorMessage
    {
        get => _mirrorMessage;
        private set => SetProperty(ref _mirrorMessage, value);
    }

    public bool IsMirrorSessionActive =>
        MirrorState is MirrorSessionState.Starting
            or MirrorSessionState.Mirroring
            or MirrorSessionState.Stopping;

    public bool CanStartMirror =>
        SelectedDevice?.State == AndroidDeviceState.Device &&
        !IsMirrorSessionActive &&
        !_mirrorService.IsRunning;

    public string? MirroredDeviceSerial =>
        _mirrorService.ActiveDeviceSerial ?? _mirrorTargetSerial;

    public string SelectedDeviceName =>
        SelectedDevice?.Model ?? SelectedDevice?.Serial ?? "Android Device";

    public string SelectedDeviceManufacturer => SelectedDevice?.Manufacturer ?? "Unknown manufacturer";

    public string SelectedAndroidVersion => SelectedDevice?.AndroidVersion is { Length: > 0 } version
        ? $"Android {version}"
        : "Android version unavailable";

    public string SelectedConnectionType => SelectedDevice?.ConnectionType switch
    {
        AndroidConnectionType.Usb => "USB",
        AndroidConnectionType.Network => "Wi-Fi",
        _ => "Unknown connection"
    };

    public string SelectedStateTitle
    {
        get
        {
            if (SelectedDevice?.Serial == (_mirrorService.ActiveDeviceSerial ?? _mirrorTargetSerial))
            {
                return MirrorState switch
                {
                    MirrorSessionState.Starting => "Starting mirror...",
                    MirrorSessionState.Mirroring => "Mirroring",
                    MirrorSessionState.Stopping => "Stopping mirror...",
                    _ => GetDeviceStateTitle()
                };
            }

            return GetDeviceStateTitle();
        }
    }

    public string SelectedStateDescription => SelectedDevice?.State switch
    {
        AndroidDeviceState.Unauthorized =>
            "Unlock the Android device and accept the USB debugging authorization.",
        AndroidDeviceState.Offline => "Reconnect the device or restart USB debugging.",
        AndroidDeviceState.Disconnected => "The device is no longer connected.",
        AndroidDeviceState.Device => "The Android device is ready.",
        _ => "The device state could not be determined."
    };

    public async Task StartMirrorAsync()
    {
        await _mirrorOperationGate.WaitAsync(_lifetimeCancellationSource.Token);
        try
        {
            var device = SelectedDevice;
            if (device is null || device.State != AndroidDeviceState.Device || IsMirrorSessionActive)
            {
                return;
            }

            _mirrorTargetSerial = device.Serial;
            OnPropertyChanged(nameof(MirroredDeviceSerial));
            MirrorState = MirrorSessionState.Starting;
            MirrorMessage = null;
            var sessionSettings = _settings;

            var result = await _mirrorService.StartAsync(
                device,
                sessionSettings,
                _lifetimeCancellationSource.Token);
            if (!result.IsSuccess)
            {
                MirrorState = MirrorSessionState.Error;
                _mirrorTargetSerial = null;
                OnPropertyChanged(nameof(MirroredDeviceSerial));
                MirrorMessage = sessionSettings.QualityProfile == MirrorQualityProfile.Quality
                    ? "Unable to start mirroring with the selected quality profile. Try Balanced mode."
                    : "Unable to start screen mirroring.";
                DiagnosticMessage = result.ErrorMessage;
                return;
            }

            if (_mirrorService.IsRunning)
            {
                MirrorState = MirrorSessionState.Mirroring;
                MirrorMessage = "Mouse & keyboard control enabled.";
            }
            else if (MirrorState == MirrorSessionState.Starting)
            {
                MirrorState = MirrorSessionState.Ready;
                _mirrorTargetSerial = null;
                OnPropertyChanged(nameof(MirroredDeviceSerial));
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellationSource.IsCancellationRequested)
        {
            MirrorState = MirrorSessionState.Ready;
            _mirrorTargetSerial = null;
            OnPropertyChanged(nameof(MirroredDeviceSerial));
        }
        catch (Exception exception)
        {
            MirrorState = MirrorSessionState.Error;
            _mirrorTargetSerial = null;
            OnPropertyChanged(nameof(MirroredDeviceSerial));
            MirrorMessage = "Unable to start screen mirroring.";
            DiagnosticMessage = exception.Message;
        }
        finally
        {
            _mirrorOperationGate.Release();
        }
    }

    public async Task StopMirrorAsync()
    {
        await _mirrorOperationGate.WaitAsync(_lifetimeCancellationSource.Token);
        try
        {
            if (!IsMirrorSessionActive && !_mirrorService.IsRunning)
            {
                return;
            }

            MirrorState = MirrorSessionState.Stopping;

            var result = await _mirrorService.StopAsync(_lifetimeCancellationSource.Token);
            if (!result.IsSuccess)
            {
                MirrorState = MirrorSessionState.Error;
                MirrorMessage = "Unable to stop screen mirroring.";
                DiagnosticMessage = result.ErrorMessage;
            }
            else if (!_mirrorService.IsRunning)
            {
                MirrorState = MirrorSessionState.Ready;
                MirrorMessage = "Screen mirroring stopped.";
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellationSource.IsCancellationRequested)
        {
            // Application shutdown continues cleanup through IMirrorService.DisposeAsync.
        }
        catch (Exception exception)
        {
            MirrorState = MirrorSessionState.Error;
            MirrorMessage = "Unable to stop screen mirroring.";
            DiagnosticMessage = exception.Message;
        }
        finally
        {
            _mirrorOperationGate.Release();
        }
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        _isInitialized = true;
        _settings = await _settingsStore.LoadAsync(_lifetimeCancellationSource.Token);
        NotifySettingsProperties();
        try
        {
            await _wirelessAdbService.ReconnectKnownDevicesAsync(
                _lifetimeCancellationSource.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellationSource.IsCancellationRequested)
        {
            return;
        }

        await RefreshAsync();
    }

    public WirelessPairingViewModel CreateWirelessPairingViewModel()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var viewModel = new WirelessPairingViewModel(_wirelessAdbService);
        viewModel.PairingCompleted += WirelessPairing_PairingCompleted;
        return viewModel;
    }

    public async Task RefreshAsync()
    {
        if (_isDisposed || !await _refreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            IsLoading = true;
            var cancellationToken = _lifetimeCancellationSource.Token;
            var availability = await _adbService.CheckAvailabilityAsync(cancellationToken);
            if (!availability.IsAvailable)
            {
                Devices.Clear();
                SelectedDevice = null;
                AdbStatus = "ADB not found";
                StatusMessage = "ADB not found. Configure Android Platform Tools.";
                DiagnosticMessage = availability.ErrorMessage;
                return;
            }

            AdbStatus = "ADB Ready";
            var result = await _adbService.GetDevicesAsync(cancellationToken);
            if (!result.IsSuccess)
            {
                StatusMessage = "Unable to communicate with ADB.";
                DiagnosticMessage = result.ErrorMessage;
                return;
            }

            ReconcileDevices(result.Devices);
            var newlyDetectedReadyDevices = result.Devices
                .Where(device =>
                    device.State == AndroidDeviceState.Device &&
                    !_knownDeviceSerials.Contains(device.Serial))
                .ToArray();
            _knownDeviceSerials.Clear();
            foreach (var device in result.Devices)
            {
                _knownDeviceSerials.Add(device.Serial);
            }
            DiagnosticMessage = result.Warnings.Count == 0
                ? null
                : string.Join(Environment.NewLine, result.Warnings);
            StatusMessage = Devices.Count switch
            {
                0 => "No Android device detected. Connect by USB or enable Wireless debugging.",
                1 => "1 device detected",
                _ => $"{Devices.Count} devices detected"
            };

            await TryStartAutoMirrorAsync(result.Devices, newlyDetectedReadyDevices);
        }
        catch (OperationCanceledException) when (_lifetimeCancellationSource.IsCancellationRequested)
        {
            // Application shutdown cancels any in-flight ADB operation.
        }
        catch (Exception exception)
        {
            StatusMessage = "Unable to communicate with ADB.";
            DiagnosticMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
            _refreshGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await _lifetimeCancellationSource.CancelAsync();

        await _refreshGate.WaitAsync();
        _refreshGate.Release();

        Task pendingSave;
        lock (_settingsSaveSync)
        {
            pendingSave = _settingsSaveTask;
        }
        await pendingSave;

        _mirrorService.SessionEnded -= MirrorService_SessionEnded;
        await _mirrorService.DisposeAsync();

        _lifetimeCancellationSource.Dispose();
        _refreshGate.Dispose();
        _mirrorOperationGate.Dispose();
    }

    private void ReconcileDevices(IReadOnlyList<AndroidDevice> updatedDevices)
    {
        var selectedSerial = SelectedDevice?.Serial;
        var updatedSerials = updatedDevices
            .Select(device => device.Serial)
            .ToHashSet(StringComparer.Ordinal);

        for (var index = Devices.Count - 1; index >= 0; index--)
        {
            if (!updatedSerials.Contains(Devices[index].Serial))
            {
                Devices.RemoveAt(index);
            }
        }

        for (var updatedIndex = 0; updatedIndex < updatedDevices.Count; updatedIndex++)
        {
            var updatedDevice = updatedDevices[updatedIndex];
            var existingIndex = FindDeviceIndex(updatedDevice.Serial);

            if (existingIndex < 0)
            {
                Devices.Insert(Math.Min(updatedIndex, Devices.Count), updatedDevice);
            }
            else
            {
                if (Devices[existingIndex] != updatedDevice)
                {
                    Devices[existingIndex] = updatedDevice;
                }

                if (existingIndex != updatedIndex)
                {
                    Devices.Move(existingIndex, updatedIndex);
                }
            }
        }

        SelectedDevice = selectedSerial is null
            ? Devices.FirstOrDefault()
            : Devices.FirstOrDefault(device => device.Serial == selectedSerial) ?? Devices.FirstOrDefault();

        OnPropertyChanged(nameof(Devices));
    }

    private int FindDeviceIndex(string serial)
    {
        for (var index = 0; index < Devices.Count; index++)
        {
            if (string.Equals(Devices[index].Serial, serial, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void NotifySelectedDeviceProperties()
    {
        OnPropertyChanged(nameof(SelectedDeviceName));
        OnPropertyChanged(nameof(SelectedDeviceManufacturer));
        OnPropertyChanged(nameof(SelectedAndroidVersion));
        OnPropertyChanged(nameof(SelectedConnectionType));
        OnPropertyChanged(nameof(SelectedStateTitle));
        OnPropertyChanged(nameof(SelectedStateDescription));
    }

    private string GetDeviceStateTitle() => SelectedDevice?.State switch
    {
        AndroidDeviceState.Device => "Connected",
        AndroidDeviceState.Unauthorized => "Authorization required",
        AndroidDeviceState.Offline => "Device offline",
        AndroidDeviceState.Disconnected => "Device disconnected",
        _ => "Unknown device state"
    };

    private void MirrorService_SessionEnded(object? sender, MirrorSessionEndedEventArgs e)
    {
        void ApplyResult()
        {
            if (_isDisposed)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(e.ErrorMessage) && !e.WasStopped)
            {
                _mirrorTargetSerial = null;
                OnPropertyChanged(nameof(MirroredDeviceSerial));
                MirrorState = MirrorSessionState.Error;
                MirrorMessage = IsInputPermissionError(e.ErrorMessage)
                    ? "Android blocked input control. Some devices require an additional debugging/security option in Developer options."
                    : "Screen mirroring ended unexpectedly.";
                DiagnosticMessage = e.ErrorMessage;
            }
            else
            {
                _mirrorTargetSerial = null;
                OnPropertyChanged(nameof(MirroredDeviceSerial));
                MirrorState = MirrorSessionState.Ready;
                MirrorMessage = e.WasStopped
                    ? "Screen mirroring stopped."
                    : "Screen mirroring window was closed.";
            }
        }

        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
        {
            ApplyResult();
        }
        else
        {
            _synchronizationContext.Post(_ => ApplyResult(), null);
        }
    }

    private void NotifyMirrorCommandState()
    {
        MirrorCommand.RaiseCanExecuteChanged();
        StopMirrorCommand.RaiseCanExecuteChanged();
    }

    private async Task TryStartAutoMirrorAsync(
        IReadOnlyList<AndroidDevice> devices,
        IReadOnlyList<AndroidDevice> newlyDetectedReadyDevices)
    {
        if (!AutoMirror ||
            IsMirrorSessionActive ||
            _mirrorService.IsRunning ||
            newlyDetectedReadyDevices.Count != 1 ||
            devices.Count(device => device.State == AndroidDeviceState.Device) != 1)
        {
            return;
        }

        SelectedDevice = newlyDetectedReadyDevices[0];
        await StartMirrorAsync();
    }

    private void SettingsChanged()
    {
        SettingsChangeMessage = IsMirrorSessionActive
            ? "Changes will apply to the next mirror session."
            : null;
        var snapshot = _settings;
        lock (_settingsSaveSync)
        {
            _settingsSaveTask = SaveSettingsAfterAsync(_settingsSaveTask, snapshot);
        }
    }

    private async Task SaveSettingsAfterAsync(Task previousSave, MirrorSettings settings)
    {
        try
        {
            await previousSave;
            await _settingsStore.SaveAsync(settings, CancellationToken.None);
        }
        catch (Exception exception)
        {
            DiagnosticMessage = $"Unable to save settings: {exception.Message}";
        }
    }

    private void NotifySettingsProperties()
    {
        OnPropertyChanged(nameof(QualityProfile));
        OnPropertyChanged(nameof(TurnScreenOff));
        OnPropertyChanged(nameof(StayAwake));
        OnPropertyChanged(nameof(AutoMirror));
    }

    private async void WirelessPairing_PairingCompleted(object? sender, EventArgs e)
    {
        if (sender is WirelessPairingViewModel viewModel)
        {
            viewModel.PairingCompleted -= WirelessPairing_PairingCompleted;
        }

        await RefreshAsync();
    }

    private static bool IsInputPermissionError(string errorMessage) =>
        (errorMessage.Contains("inject", StringComparison.OrdinalIgnoreCase) ||
         errorMessage.Contains("input event", StringComparison.OrdinalIgnoreCase)) &&
        (errorMessage.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
         errorMessage.Contains("SecurityException", StringComparison.OrdinalIgnoreCase));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
