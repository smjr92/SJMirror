using System.IO;

namespace SJMirror.App.Services;

public sealed class ScrcpyPathResolver : IScrcpyPathResolver
{
    public const string ScrcpyPathEnvironmentVariable = "SJMIRROR_SCRCPY_PATH";

    private readonly string? _configuredPath;
    private readonly string _applicationBaseDirectory;

    public ScrcpyPathResolver(
        string? configuredPath = null,
        string? applicationBaseDirectory = null)
    {
        _configuredPath = configuredPath;
        _applicationBaseDirectory = applicationBaseDirectory ?? AppContext.BaseDirectory;
    }

    public ScrcpyPathResolution Resolve()
    {
        if (!string.IsNullOrWhiteSpace(_configuredPath))
        {
            return ResolveCandidate(_configuredPath, "configured scrcpy path");
        }

        var environmentPath = Environment.GetEnvironmentVariable(ScrcpyPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return ResolveCandidate(
                environmentPath,
                $"{ScrcpyPathEnvironmentVariable} environment variable");
        }

        var bundledPath = Path.Combine(_applicationBaseDirectory, "tools", "scrcpy", "scrcpy.exe");
        return ResolveCandidate(bundledPath, "bundled scrcpy path");
    }

    private static ScrcpyPathResolution ResolveCandidate(string candidate, string source)
    {
        try
        {
            var fullPath = Path.GetFullPath(candidate);
            return File.Exists(fullPath)
                ? new ScrcpyPathResolution(fullPath, null)
                : new ScrcpyPathResolution(
                    null,
                    $"scrcpy was not found at the {source}: '{fullPath}'.");
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new ScrcpyPathResolution(null, $"The {source} is invalid: {exception.Message}");
        }
    }
}
