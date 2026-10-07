namespace SJMirror.App.Services;

public sealed class MirrorSessionEndedEventArgs(
    string deviceSerial,
    int? exitCode,
    string standardOutput,
    string standardError,
    bool wasStopped,
    string? errorMessage) : EventArgs
{
    public string DeviceSerial { get; } = deviceSerial;

    public int? ExitCode { get; } = exitCode;

    public string StandardOutput { get; } = standardOutput;

    public string StandardError { get; } = standardError;

    public bool WasStopped { get; } = wasStopped;

    public string? ErrorMessage { get; } = errorMessage;
}
