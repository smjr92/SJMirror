namespace SJMirror.App.Services;

public sealed record AdbPathResolution(string? ExecutablePath, string? ErrorMessage)
{
    public bool IsResolved => ExecutablePath is not null;
}
