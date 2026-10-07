namespace SJMirror.App.Services;

public interface IManagedProcess : IAsyncDisposable
{
    bool HasExited { get; }

    Task<ManagedProcessResult> Completion { get; }

    Task<ManagedProcessResult> StopAsync(CancellationToken cancellationToken = default);
}
