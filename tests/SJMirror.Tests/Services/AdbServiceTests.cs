using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class AdbServiceTests
{
    private const string AdbPath = @"C:\test-tools\adb.exe";

    [Fact]
    public async Task CheckAvailabilityAsyncReturnsAvailableWhenVersionCommandSucceeds()
    {
        var processRunner = new FakeProcessRunner((_, _, _, _) =>
            Task.FromResult(new ProcessResult(0, "Android Debug Bridge version 1.0.41", string.Empty)));
        var service = CreateService(processRunner);

        var result = await service.CheckAvailabilityAsync();

        Assert.True(result.IsAvailable);
        Assert.Equal(AdbPath, result.ExecutablePath);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(["version"], Assert.Single(processRunner.Invocations).Arguments);
    }

    [Fact]
    public async Task CheckAvailabilityAsyncReturnsControlledResultWhenAdbIsMissing()
    {
        var processRunner = new FakeProcessRunner((_, _, _, _) =>
            throw new InvalidOperationException("The process runner should not be called."));
        var pathResolver = new FakeAdbPathResolver(
            new AdbPathResolution(null, "ADB was not found."));
        var service = new AdbService(processRunner, pathResolver);

        var result = await service.CheckAvailabilityAsync();

        Assert.False(result.IsAvailable);
        Assert.Null(result.ExecutablePath);
        Assert.Equal("ADB was not found.", result.ErrorMessage);
        Assert.Empty(processRunner.Invocations);
    }

    [Fact]
    public async Task GetVersionAsyncReturnsVersionOutput()
    {
        const string version = "Android Debug Bridge version 1.0.41";
        var processRunner = new FakeProcessRunner((_, _, _, _) =>
            Task.FromResult(new ProcessResult(0, version, string.Empty)));
        var service = CreateService(processRunner);

        var result = await service.GetVersionAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(version, result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task GetVersionAsyncReturnsFailureWhenProcessExitCodeIsNonzero()
    {
        var processRunner = new FakeProcessRunner((_, _, _, _) =>
            Task.FromResult(new ProcessResult(1, string.Empty, "ADB failed")));
        var service = CreateService(processRunner);

        var result = await service.GetVersionAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("ADB failed", result.StandardError);
        Assert.Contains("ADB exited with code 1", result.ErrorMessage);
    }

    [Fact]
    public async Task CommandsPassArgumentsSeparatelyToProcessRunner()
    {
        var processRunner = new FakeProcessRunner((_, _, _, _) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty)));
        var service = CreateService(processRunner);

        await service.StartServerAsync();
        await service.KillServerAsync();

        Assert.Collection(
            processRunner.Invocations,
            invocation => Assert.Equal(["start-server"], invocation.Arguments),
            invocation => Assert.Equal(["kill-server"], invocation.Arguments));
    }

    [Fact]
    public async Task GetVersionAsyncPropagatesRequestedCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var processRunner = new FakeProcessRunner((_, _, _, cancellationToken) =>
            Task.FromCanceled<ProcessResult>(cancellationToken));
        var service = CreateService(processRunner);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetVersionAsync(cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }

    private static AdbService CreateService(FakeProcessRunner processRunner) =>
        new(
            processRunner,
            new FakeAdbPathResolver(new AdbPathResolution(AdbPath, null)));

    private sealed class FakeAdbPathResolver(AdbPathResolution resolution) : IAdbPathResolver
    {
        public AdbPathResolution Resolve() => resolution;
    }

    private sealed class FakeProcessRunner(
        Func<string, IEnumerable<string>?, TimeSpan?, CancellationToken, Task<ProcessResult>> handler)
        : IProcessRunner
    {
        public List<ProcessInvocation> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            string executablePath,
            IEnumerable<string>? arguments = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var argumentList = arguments?.ToArray() ?? [];
            Invocations.Add(new ProcessInvocation(executablePath, argumentList, timeout));
            return handler(executablePath, argumentList, timeout, cancellationToken);
        }
    }

    private sealed record ProcessInvocation(
        string ExecutablePath,
        IReadOnlyList<string> Arguments,
        TimeSpan? Timeout);
}
