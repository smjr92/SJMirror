namespace SJMirror.App.Services;

public sealed record AdbCommandResult(
    bool IsSuccess,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string? ErrorMessage);
