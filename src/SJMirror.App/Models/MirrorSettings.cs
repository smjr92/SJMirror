namespace SJMirror.App.Models;

public sealed record MirrorSettings
{
    public MirrorQualityProfile QualityProfile { get; init; } = MirrorQualityProfile.Balanced;

    public bool TurnScreenOff { get; init; }

    public bool StayAwake { get; init; } = true;

    public bool AutoMirror { get; init; }
}
