using SJMirror.App.Models;

namespace SJMirror.App.Services;

public sealed record AdbDevicesParseResult(
    bool IsValid,
    IReadOnlyList<AndroidDevice> Devices,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage);
