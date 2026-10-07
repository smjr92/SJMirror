using System.Text;
using SJMirror.App.Models;
using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class WirelessAdbServiceTests
{
    private const string AdbPath = @"C:\test-tools\adb.exe";

    [Fact]
    public async Task QrPairingDiscoversMatchingServiceAndPairsWithoutSecretArgument()
    {
        var runner = RunnerWithServices();
        var sensitiveRunner = new FakeSensitiveProcessRunner();
        var renderer = new FakeQrRenderer();
        var service = CreateService(runner, sensitiveRunner, renderer);
        var session = await service.CreateQrPairingSessionAsync(TimeSpan.FromSeconds(1));
        runner.ServiceName = session.ServiceName;

        var result = await service.WaitForQrPairingAsync(session);

        Assert.True(result.IsSuccess);
        var invocation = Assert.Single(sensitiveRunner.Invocations);
        Assert.Equal(["pair", "192.168.1.40:37125"], invocation.Arguments);
        Assert.DoesNotContain(invocation.StandardInput, invocation.Arguments);
        Assert.DoesNotContain(invocation.StandardInput, result.ErrorMessage ?? string.Empty);
        Assert.False(session.IsValid);
        Assert.StartsWith($"WIFI:T:ADB;S:{session.ServiceName};P:", renderer.Payload);
    }

    [Fact]
    public async Task QrPairingTimesOutAndInvalidatesSecret()
    {
        var service = CreateService(new FakeProcessRunner(), new FakeSensitiveProcessRunner());
        var session = await service.CreateQrPairingSessionAsync(TimeSpan.FromMilliseconds(30));

        var result = await service.WaitForQrPairingAsync(session);

        Assert.False(result.IsSuccess);
        Assert.Equal(WirelessPairingState.TimedOut, result.State);
        Assert.False(session.IsValid);
    }

    [Fact]
    public async Task QrPairingCanBeCancelled()
    {
        var service = CreateService(new FakeProcessRunner(), new FakeSensitiveProcessRunner());
        var session = await service.CreateQrPairingSessionAsync(TimeSpan.FromSeconds(2));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var result = await service.WaitForQrPairingAsync(session, cancellationSource.Token);

        Assert.Equal(WirelessPairingState.Cancelled, result.State);
        Assert.False(session.IsValid);
    }

    [Fact]
    public async Task CreatingNewQrSessionInvalidatesPreviousSecret()
    {
        var service = CreateService(new FakeProcessRunner(), new FakeSensitiveProcessRunner());
        var first = await service.CreateQrPairingSessionAsync(TimeSpan.FromSeconds(2));

        var second = await service.CreateQrPairingSessionAsync(TimeSpan.FromSeconds(2));

        Assert.False(first.IsValid);
        Assert.True(second.IsValid);
        Assert.NotEqual(first.ServiceName, second.ServiceName);
    }

    [Fact]
    public async Task QrPairingReturnsControlledFailure()
    {
        var runner = RunnerWithServices();
        var sensitiveRunner = new FakeSensitiveProcessRunner
        {
            Result = new ProcessResult(1, string.Empty, "pairing rejected")
        };
        var service = CreateService(runner, sensitiveRunner);
        var session = await service.CreateQrPairingSessionAsync(TimeSpan.FromSeconds(1));
        runner.ServiceName = session.ServiceName;

        var result = await service.WaitForQrPairingAsync(session);

        Assert.False(result.IsSuccess);
        Assert.Equal(WirelessPairingState.Failed, result.State);
        Assert.DoesNotContain(sensitiveRunner.Invocations[0].StandardInput, result.ErrorMessage!);
    }

    [Fact]
    public async Task NumericPairingUsesStdinAndNeverCommandLine()
    {
        var sensitiveRunner = new FakeSensitiveProcessRunner();
        var service = CreateService(RunnerWithServices(), sensitiveRunner);

        var result = await service.PairWithCodeAsync("192.168.1.40", 37125, "123456");

        Assert.True(result.IsSuccess);
        var invocation = Assert.Single(sensitiveRunner.Invocations);
        Assert.Equal(["pair", "192.168.1.40:37125"], invocation.Arguments);
        Assert.Equal("123456", invocation.StandardInput);
        Assert.DoesNotContain("123456", invocation.Arguments);
    }

    [Fact]
    public async Task NumericPairingRejectsNonLocalAddress()
    {
        var sensitiveRunner = new FakeSensitiveProcessRunner();
        var service = CreateService(new FakeProcessRunner(), sensitiveRunner);

        var result = await service.PairWithCodeAsync("8.8.8.8", 37125, "123456");

        Assert.False(result.IsSuccess);
        Assert.Empty(sensitiveRunner.Invocations);
    }

    [Fact]
    public async Task ReconnectUsesDiscoveredTlsConnectEndpoint()
    {
        var runner = RunnerWithServices();
        var service = CreateService(runner, new FakeSensitiveProcessRunner());

        await service.ReconnectKnownDevicesAsync();

        Assert.Contains(runner.Invocations, arguments =>
            arguments.SequenceEqual(["connect", "192.168.1.40:39877"]));
    }

    private static WirelessAdbService CreateService(
        FakeProcessRunner processRunner,
        FakeSensitiveProcessRunner sensitiveRunner,
        FakeQrRenderer? renderer = null) =>
        new(
            processRunner,
            sensitiveRunner,
            new FakeAdbPathResolver(),
            renderer ?? new FakeQrRenderer(),
            new WirelessPairingCredentialsGenerator(),
            new WirelessMdnsServicesParser(),
            TimeSpan.FromMilliseconds(5));

    private static FakeProcessRunner RunnerWithServices()
    {
        var runner = new FakeProcessRunner();
        runner.Handler = arguments =>
        {
            if (arguments.SequenceEqual(["mdns", "check"]))
            {
                return new ProcessResult(0, "mdns daemon version", string.Empty);
            }

            if (arguments.SequenceEqual(["mdns", "services"]))
            {
                return new ProcessResult(
                    0,
                    $"List of discovered mdns services\n{runner.ServiceName} _adb-tls-pairing._tcp 192.168.1.40:37125\nadb-guid _adb-tls-connect._tcp 192.168.1.40:39877\n",
                    string.Empty);
            }

            return new ProcessResult(0, "connected", string.Empty);
        };
        return runner;
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public string ServiceName { get; set; } = "not-the-session";

        public Func<IReadOnlyList<string>, ProcessResult>? Handler { get; set; }

        public List<IReadOnlyList<string>> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            string executablePath,
            IEnumerable<string>? arguments = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var argumentList = arguments?.ToArray() ?? [];
            Invocations.Add(argumentList);
            return Task.FromResult(
                Handler?.Invoke(argumentList) ??
                new ProcessResult(0, "List of discovered mdns services", string.Empty));
        }
    }

    private sealed class FakeSensitiveProcessRunner : ISensitiveProcessRunner
    {
        public ProcessResult Result { get; set; } = new(0, "Successfully paired", string.Empty);

        public List<SensitiveInvocation> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            string executablePath,
            IEnumerable<string> arguments,
            string standardInput,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(new SensitiveInvocation(arguments.ToArray(), standardInput));
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeAdbPathResolver : IAdbPathResolver
    {
        public AdbPathResolution Resolve() => new(AdbPath, null);
    }

    private sealed class FakeQrRenderer : IWirelessQrCodeRenderer
    {
        public string? Payload { get; private set; }

        public byte[] RenderPng(string payload)
        {
            Payload = payload;
            return Encoding.UTF8.GetBytes("fake png");
        }
    }

    private sealed record SensitiveInvocation(
        IReadOnlyList<string> Arguments,
        string StandardInput);
}
