using SJMirror.App.Models;

namespace SJMirror.App.Services;

public sealed class ScrcpyArgumentsBuilder
{
    public IReadOnlyList<string> Build(AndroidDevice device) =>
        Build(device, new MirrorSettings());

    public IReadOnlyList<string> Build(AndroidDevice device, MirrorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(device.Serial);

        var arguments = new List<string>
        {
            "--serial",
            device.Serial,
            "--mouse=sdk"
        };

        arguments.AddRange(settings.QualityProfile switch
        {
            MirrorQualityProfile.Performance =>
                ["--max-size=1280", "--max-fps=30", "--video-codec=h264", "--video-bit-rate=4M"],
            MirrorQualityProfile.Quality =>
                ["--max-size=2560", "--max-fps=60", "--video-codec=h265", "--video-bit-rate=12M"],
            _ =>
                ["--max-size=1920", "--max-fps=60", "--video-codec=h264", "--video-bit-rate=8M"]
        });

        if (settings.TurnScreenOff)
        {
            arguments.Add("--turn-screen-off");
        }

        if (settings.StayAwake)
        {
            arguments.Add("--stay-awake");
        }

        return arguments;
    }
}
