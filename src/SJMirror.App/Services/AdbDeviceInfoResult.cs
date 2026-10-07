using SJMirror.App.Models;

namespace SJMirror.App.Services;

public sealed record AdbDeviceInfoResult(
    bool IsSuccess,
    AndroidDevice? Device,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage);
