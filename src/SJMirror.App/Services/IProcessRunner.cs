namespace SJMirror.App.Services;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string executablePath,
        IEnumerable<string>? arguments = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
