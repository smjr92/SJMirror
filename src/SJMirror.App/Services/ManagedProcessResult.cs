namespace SJMirror.App.Services;

public sealed record ManagedProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
