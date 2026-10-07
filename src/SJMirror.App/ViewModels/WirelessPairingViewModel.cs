using System.ComponentModel;
using System.Runtime.CompilerServices;
using SJMirror.App.Infrastructure;
using SJMirror.App.Models;
using SJMirror.App.Services;

namespace SJMirror.App.ViewModels;

public sealed class WirelessPairingViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan PairingTimeout = TimeSpan.FromMinutes(2);
    private readonly IWirelessAdbService _wirelessAdbService;
    private CancellationTokenSource? _sessionCancellationSource;
    private WirelessPairingSession? _session;
    private WirelessPairingState _state = WirelessPairingState.Idle;
    private byte[]? _qrCodePng;
    private string _statusMessage = "Prepare your Android device for wireless pairing.";
    private bool _usePairingCode;
    private string _ipAddress = string.Empty;
    private string _pairingPort = string.Empty;
    private string _pairingCode = string.Empty;
    private bool _disposed;

    public WirelessPairingViewModel(IWirelessAdbService wirelessAdbService)
    {
        _wirelessAdbService = wirelessAdbService;
        GenerateQrCommand = new AsyncRelayCommand(GenerateQrAsync);
        UsePairingCodeCommand = new AsyncRelayCommand(SwitchToPairingCodeAsync);
        UseQrCodeCommand = new AsyncRelayCommand(GenerateQrAsync);
        PairWithCodeCommand = new AsyncRelayCommand(PairWithCodeAsync);
        CancelCommand = new AsyncRelayCommand(CancelAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? PairingCompleted;

    public AsyncRelayCommand GenerateQrCommand { get; }

    public AsyncRelayCommand UsePairingCodeCommand { get; }

    public AsyncRelayCommand UseQrCodeCommand { get; }

    public AsyncRelayCommand PairWithCodeCommand { get; }

    public AsyncRelayCommand CancelCommand { get; }

    public WirelessPairingState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    public byte[]? QrCodePng
    {
        get => _qrCodePng;
        private set => SetProperty(ref _qrCodePng, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool UsePairingCode
    {
        get => _usePairingCode;
        private set
        {
            if (SetProperty(ref _usePairingCode, value))
            {
                OnPropertyChanged(nameof(UseQrCode));
            }
        }
    }

    public bool UseQrCode => !UsePairingCode;

    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }

    public string PairingPort
    {
        get => _pairingPort;
        set => SetProperty(ref _pairingPort, value);
    }

    public string PairingCode
    {
        get => _pairingCode;
        set => SetProperty(ref _pairingCode, value);
    }

    public async Task GenerateQrAsync()
    {
        await CancelCurrentSessionAsync();
        UsePairingCode = false;
        State = WirelessPairingState.Preparing;
        StatusMessage = "Generating a secure pairing session...";
        _sessionCancellationSource = new CancellationTokenSource();
        var cancellationToken = _sessionCancellationSource.Token;

        try
        {
            _session = await _wirelessAdbService.CreateQrPairingSessionAsync(
                PairingTimeout,
                cancellationToken);
            QrCodePng = _session.QrCodePng;
            State = WirelessPairingState.WaitingForScan;
            StatusMessage = "Waiting for device...";

            var result = await _wirelessAdbService.WaitForQrPairingAsync(
                _session,
                cancellationToken);
            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            State = WirelessPairingState.Cancelled;
            StatusMessage = "Pairing was cancelled.";
        }
    }

    public async Task PairWithCodeAsync()
    {
        await CancelCurrentSessionAsync();
        UsePairingCode = true;
        if (!int.TryParse(PairingPort, out var port))
        {
            State = WirelessPairingState.Failed;
            StatusMessage = "Enter a valid pairing port.";
            return;
        }

        State = WirelessPairingState.Pairing;
        StatusMessage = "Pairing with Android...";
        _sessionCancellationSource = new CancellationTokenSource();
        var result = await _wirelessAdbService.PairWithCodeAsync(
            IpAddress.Trim(),
            port,
            PairingCode.Trim(),
            _sessionCancellationSource.Token);
        PairingCode = string.Empty;
        ApplyResult(result);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await CancelCurrentSessionAsync();
    }

    private async Task SwitchToPairingCodeAsync()
    {
        await CancelCurrentSessionAsync();
        UsePairingCode = true;
        State = WirelessPairingState.Idle;
        StatusMessage = "Enter the address, port and code shown by Android.";
    }

    private async Task CancelAsync()
    {
        await CancelCurrentSessionAsync();
        State = WirelessPairingState.Cancelled;
        StatusMessage = "Pairing was cancelled.";
    }

    private async Task CancelCurrentSessionAsync()
    {
        if (_session is not null)
        {
            _wirelessAdbService.Cancel(_session);
            _session = null;
        }

        if (_sessionCancellationSource is not null)
        {
            await _sessionCancellationSource.CancelAsync();
            _sessionCancellationSource.Dispose();
            _sessionCancellationSource = null;
        }

        QrCodePng = null;
    }

    private void ApplyResult(WirelessPairingResult result)
    {
        QrCodePng = null;
        State = result.State;
        StatusMessage = result.IsSuccess
            ? "Wireless device connected."
            : result.ErrorMessage ?? "Wireless pairing failed.";
        if (result.IsSuccess)
        {
            PairingCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
