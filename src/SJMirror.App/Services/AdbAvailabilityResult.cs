namespace SJMirror.App.Services;

public sealed record AdbAvailabilityResult(
    bool IsAvailable,
    string? ExecutablePath,
    string? ErrorMessage);
