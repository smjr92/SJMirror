using System.Diagnostics;
using System.IO;

namespace SJMirror.App.Services;

public sealed class ManagedProcessLauncher : IManagedProcessLauncher
{
    public IManagedProcess Start(string executablePath, IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var fullExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullExecutablePath))
        {
            throw new FileNotFoundException("The process executable was not found.", fullExecutablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fullExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(fullExecutablePath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(
                argument ?? throw new ArgumentException(
                    "Arguments cannot contain null values.",
                    nameof(arguments)));
        }

        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process '{fullExecutablePath}'.");
            }

            return new ManagedProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private sealed class ManagedProcess : IManagedProcess
    {
        private readonly Process _process;
        private readonly Task<string> _standardOutputTask;
        private readonly Task<string> _standardErrorTask;
        private bool _disposed;

        public ManagedProcess(Process process)
        {
            _process = process;
            _standardOutputTask = process.StandardOutput.ReadToEndAsync();
            _standardErrorTask = process.StandardError.ReadToEndAsync();
            Completion = ObserveCompletionAsync();
        }

        public bool HasExited => _process.HasExited;

        public Task<ManagedProcessResult> Completion { get; }

        public async Task<ManagedProcessResult> StopAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (_process.HasExited)
                {
                    // The process exited between the state check and the stop request.
                }
            }

            return await Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_process.HasExited)
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await Completion.ConfigureAwait(false);
            }

            _process.Dispose();
        }

        private async Task<ManagedProcessResult> ObserveCompletionAsync()
        {
            await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(_standardOutputTask, _standardErrorTask).ConfigureAwait(false);

            return new ManagedProcessResult(
                _process.ExitCode,
                await _standardOutputTask.ConfigureAwait(false),
                await _standardErrorTask.ConfigureAwait(false));
        }
    }
}
