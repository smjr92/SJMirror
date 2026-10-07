using SJMirror.App.Models;

namespace SJMirror.App.Services;

public sealed class ScrcpyMirrorService : IMirrorService
{
    private readonly IScrcpyPathResolver _pathResolver;
    private readonly IManagedProcessLauncher _processLauncher;
    private readonly ScrcpyArgumentsBuilder _argumentsBuilder;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);

    private MirrorSession? _activeSession;
    private bool _disposed;

    public ScrcpyMirrorService()
        : this(new ScrcpyPathResolver(), new ManagedProcessLauncher())
    {
    }

    public ScrcpyMirrorService(string scrcpyExecutablePath)
        : this(new ScrcpyPathResolver(scrcpyExecutablePath), new ManagedProcessLauncher())
    {
    }

    public ScrcpyMirrorService(
        IScrcpyPathResolver pathResolver,
        IManagedProcessLauncher processLauncher)
        : this(pathResolver, processLauncher, new ScrcpyArgumentsBuilder())
    {
    }

    public ScrcpyMirrorService(
        IScrcpyPathResolver pathResolver,
        IManagedProcessLauncher processLauncher,
        ScrcpyArgumentsBuilder argumentsBuilder)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _argumentsBuilder = argumentsBuilder ?? throw new ArgumentNullException(nameof(argumentsBuilder));
    }

    public event EventHandler<MirrorSessionEndedEventArgs>? SessionEnded;

    public bool IsRunning => _activeSession is { Process.HasExited: false };

    public string? ActiveDeviceSerial => _activeSession?.DeviceSerial;

    public async Task<MirrorOperationResult> StartAsync(
        AndroidDevice device,
        CancellationToken cancellationToken = default) =>
        await StartAsync(device, new MirrorSettings(), cancellationToken).ConfigureAwait(false);

    public async Task<MirrorOperationResult> StartAsync(
        AndroidDevice device,
        MirrorSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        if (device.State != AndroidDeviceState.Device)
        {
            return MirrorOperationResult.Failure("The selected Android device is not ready.");
        }

        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_activeSession is not null)
            {
                return MirrorOperationResult.Failure("A screen mirroring session is already active.");
            }

            var resolution = _pathResolver.Resolve();
            if (!resolution.IsResolved)
            {
                return MirrorOperationResult.Failure(
                    resolution.ErrorMessage ?? "scrcpy was not found.");
            }

            IManagedProcess process;
            try
            {
                process = _processLauncher.Start(
                    resolution.ExecutablePath!,
                    _argumentsBuilder.Build(device, settings));
            }
            catch (Exception exception)
            {
                return MirrorOperationResult.Failure($"Unable to start scrcpy: {exception.Message}");
            }

            var session = new MirrorSession(device.Serial, process);
            _activeSession = session;
            session.ObservationTask = ObserveSessionAsync(session);
            return MirrorOperationResult.Success;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task<MirrorOperationResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        Task<MirrorOperationResult>? stopTask;

        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = _activeSession;
            if (session is null)
            {
                return MirrorOperationResult.Success;
            }

            session.StopRequested = true;
            session.StopTask ??= StopSessionAsync(session);
            stopTask = session.StopTask;
        }
        finally
        {
            _sessionGate.Release();
        }

        return await stopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<MirrorOperationResult> StopSessionAsync(MirrorSession session)
    {
        try
        {
            var result = await session.Process.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await FinalizeSessionAsync(session, result, null).ConfigureAwait(false);
            return MirrorOperationResult.Success;
        }
        catch (Exception exception)
        {
            return MirrorOperationResult.Failure($"Unable to stop scrcpy: {exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var session = _activeSession;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);

        if (session?.ObservationTask is not null)
        {
            await session.ObservationTask.ConfigureAwait(false);
        }

        _sessionGate.Dispose();
    }

    private async Task ObserveSessionAsync(MirrorSession session)
    {
        try
        {
            var result = await session.Process.Completion.ConfigureAwait(false);
            await FinalizeSessionAsync(session, result, null).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await FinalizeSessionAsync(session, null, exception).ConfigureAwait(false);
        }
    }

    private async Task FinalizeSessionAsync(
        MirrorSession session,
        ManagedProcessResult? result,
        Exception? exception)
    {
        var shouldNotify = false;

        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_activeSession, session))
            {
                _activeSession = null;
                shouldNotify = true;
            }
        }
        finally
        {
            _sessionGate.Release();
        }

        if (!shouldNotify)
        {
            return;
        }

        await session.Process.DisposeAsync().ConfigureAwait(false);

        var errorMessage = exception?.Message;
        if (errorMessage is null && result is { ExitCode: not 0 })
        {
            errorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"scrcpy exited with code {result.ExitCode}."
                : result.StandardError.Trim();
        }

        SessionEnded?.Invoke(
            this,
            new MirrorSessionEndedEventArgs(
                session.DeviceSerial,
                result?.ExitCode,
                result?.StandardOutput ?? string.Empty,
                result?.StandardError ?? string.Empty,
                session.StopRequested,
                errorMessage));
    }

    private sealed class MirrorSession(string deviceSerial, IManagedProcess process)
    {
        public string DeviceSerial { get; } = deviceSerial;

        public IManagedProcess Process { get; } = process;

        public bool StopRequested { get; set; }

        public Task? ObservationTask { get; set; }

        public Task<MirrorOperationResult>? StopTask { get; set; }
    }
}
