using System.IO;

namespace SJMirror.App.Services;

public sealed class AdbPathResolver : IAdbPathResolver
{
    public const string AdbPathEnvironmentVariable = "SJMIRROR_ADB_PATH";

    private readonly string? _configuredPath;
    private readonly string _applicationBaseDirectory;

    public AdbPathResolver(
        string? configuredPath = null,
        string? applicationBaseDirectory = null)
    {
        _configuredPath = configuredPath;
        _applicationBaseDirectory = applicationBaseDirectory ?? AppContext.BaseDirectory;
    }

    public AdbPathResolution Resolve()
    {
        if (!string.IsNullOrWhiteSpace(_configuredPath))
        {
            return ResolveCandidate(_configuredPath, "configured ADB path");
        }

        var environmentPath = Environment.GetEnvironmentVariable(AdbPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return ResolveCandidate(environmentPath, $"{AdbPathEnvironmentVariable} environment variable");
        }

        var bundledPath = Path.Combine(_applicationBaseDirectory, "tools", "adb", "adb.exe");
        return ResolveCandidate(bundledPath, "bundled ADB path");
    }

    private static AdbPathResolution ResolveCandidate(string candidate, string source)
    {
        try
        {
            var fullPath = Path.GetFullPath(candidate);
            return File.Exists(fullPath)
                ? new AdbPathResolution(fullPath, null)
                : new AdbPathResolution(null, $"ADB was not found at the {source}: '{fullPath}'.");
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new AdbPathResolution(null, $"The {source} is invalid: {exception.Message}");
        }
    }
}
