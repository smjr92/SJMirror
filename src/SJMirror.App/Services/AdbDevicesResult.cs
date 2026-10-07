using SJMirror.App.Models;

namespace SJMirror.App.Services;

public sealed record AdbDevicesResult(
    bool IsSuccess,
    IReadOnlyList<AndroidDevice> Devices,
    IReadOnlyList<string> Warnings,
    int? ExitCode,
    string StandardError,
    string? ErrorMessage);
