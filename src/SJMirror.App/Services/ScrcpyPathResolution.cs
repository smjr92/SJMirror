namespace SJMirror.App.Services;

public sealed record ScrcpyPathResolution(string? ExecutablePath, string? ErrorMessage)
{
    public bool IsResolved => ExecutablePath is not null;
}
