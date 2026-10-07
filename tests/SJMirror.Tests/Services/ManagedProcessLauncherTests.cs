using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class ManagedProcessLauncherTests
{
    private static readonly string PowerShellPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32",
        "WindowsPowerShell",
        "v1.0",
        "powershell.exe");

    [Fact]
    public async Task CompletionCapturesOutputErrorAndExitCode()
    {
        var launcher = new ManagedProcessLauncher();
        await using var process = launcher.Start(
            PowerShellPath,
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "[Console]::Out.Write('output'); [Console]::Error.Write('error'); exit 7"
            ]);

        var result = await process.Completion;

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("output", result.StandardOutput);
        Assert.Equal("error", result.StandardError);
    }

    [Fact]
    public async Task StopAsyncTerminatesSpecificProcess()
    {
        var launcher = new ManagedProcessLauncher();
        await using var process = launcher.Start(
            PowerShellPath,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"]);

        await process.StopAsync();

        Assert.True(process.HasExited);
    }
}
