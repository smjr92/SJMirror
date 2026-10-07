namespace SJMirror.App.Services;

public interface IManagedProcessLauncher
{
    IManagedProcess Start(string executablePath, IEnumerable<string> arguments);
}
