namespace SJMirror.App.Services;

public interface ISensitiveProcessRunner
{
    Task<ProcessResult> RunAsync(
        string executablePath,
        IEnumerable<string> arguments,
        string standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
