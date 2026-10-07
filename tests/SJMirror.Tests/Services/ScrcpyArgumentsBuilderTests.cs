using SJMirror.App.Models;
using SJMirror.App.Services;

namespace SJMirror.Tests.Services;

public sealed class ScrcpyArgumentsBuilderTests
{
    [Fact]
    public void BuildEnablesSdkMouseForSelectedDevice()
    {
        var device = new AndroidDevice(
            "ABC123",
            AndroidDeviceState.Device,
            "Manufacturer",
            "Model",
            "16",
            AndroidConnectionType.Usb);

        var arguments = new ScrcpyArgumentsBuilder().Build(device, new MirrorSettings());

        Assert.Equal(
            [
                "--serial", "ABC123", "--mouse=sdk", "--max-size=1920",
                "--max-fps=60", "--video-codec=h264", "--video-bit-rate=8M",
                "--stay-awake"
            ],
            arguments);
        Assert.DoesNotContain("--no-control", arguments);
        Assert.DoesNotContain("--mouse=disabled", arguments);
    }

    [Theory]
    [InlineData(MirrorQualityProfile.Performance, "--max-size=1280", "--max-fps=30", "--video-codec=h264", "--video-bit-rate=4M")]
    [InlineData(MirrorQualityProfile.Balanced, "--max-size=1920", "--max-fps=60", "--video-codec=h264", "--video-bit-rate=8M")]
    [InlineData(MirrorQualityProfile.Quality, "--max-size=2560", "--max-fps=60", "--video-codec=h265", "--video-bit-rate=12M")]
    public void BuildAppliesQualityProfile(
        MirrorQualityProfile profile,
        string maxSize,
        string maxFps,
        string codec,
        string bitRate)
    {
        var settings = new MirrorSettings
        {
            QualityProfile = profile,
            StayAwake = false
        };

        var arguments = new ScrcpyArgumentsBuilder().Build(Device(), settings);

        Assert.Contains(maxSize, arguments);
        Assert.Contains(maxFps, arguments);
        Assert.Contains(codec, arguments);
        Assert.Contains(bitRate, arguments);
        Assert.Equal("ABC123", arguments[1]);
        Assert.Contains("--mouse=sdk", arguments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildAppliesTurnScreenOffOnlyWhenEnabled(bool enabled)
    {
        var arguments = new ScrcpyArgumentsBuilder().Build(
            Device(),
            new MirrorSettings { TurnScreenOff = enabled });

        Assert.Equal(enabled, arguments.Contains("--turn-screen-off"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildAppliesStayAwakeOnlyWhenEnabled(bool enabled)
    {
        var arguments = new ScrcpyArgumentsBuilder().Build(
            Device(),
            new MirrorSettings { StayAwake = enabled });

        Assert.Equal(enabled, arguments.Contains("--stay-awake"));
    }

    private static AndroidDevice Device() =>
        new(
            "ABC123",
            AndroidDeviceState.Device,
            "Manufacturer",
            "Model",
            "16",
            AndroidConnectionType.Usb);
}
