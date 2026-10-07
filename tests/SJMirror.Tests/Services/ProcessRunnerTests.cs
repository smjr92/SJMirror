using System.Diagnostics;
using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class ProcessRunnerTests
{
    private static readonly string PowerShellPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32",
        "WindowsPowerShell",
        "v1.0",
        "powershell.exe");

    [Fact]
    public async Task RunAsyncCapturesOutputErrorAndExitCode()
    {
        var runner = new ProcessRunner();
        string[] arguments =
        [
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            "[Console]::Out.Write('standard-output'); " +
            "[Console]::Error.Write('standard-error'); exit 7"
        ];

        var result = await runner.RunAsync(PowerShellPath, arguments);

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("standard-output", result.StandardOutput);
        Assert.Equal("standard-error", result.StandardError);
    }

    [Fact]
    public async Task RunAsyncHonorsCancellation()
    {
        var runner = new ProcessRunner();
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(
                PowerShellPath,
                SleepArguments,
                cancellationToken: cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task RunAsyncHonorsTimeout()
    {
        var runner = new ProcessRunner();
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            runner.RunAsync(
                PowerShellPath,
                SleepArguments,
                timeout: TimeSpan.FromMilliseconds(250)));

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsyncRejectsMissingExecutable()
    {
        var runner = new ProcessRunner();
        var missingExecutable = Path.Combine(
            Path.GetTempPath(),
            $"sjmirror-missing-{Guid.NewGuid():N}.exe");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            runner.RunAsync(missingExecutable));
    }

    private static string[] SleepArguments =>
    [
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-Command",
        "Start-Sleep -Seconds 30"
    ];
}
