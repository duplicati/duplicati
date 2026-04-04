using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Proprietary.DiskImage.General;

/// <summary>
/// Utility class for running external processes and capturing their output, error, and exit code.
/// </summary>
public class ProcessRunner
{
    /// <summary>
    /// Log tag for process runner related messages.
    /// </summary>
    private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType<ProcessRunner>();

    /// <summary>
    /// Runs an external process with the specified command and arguments, capturing its output, error, and exit code.
    /// </summary>
    /// <param name="command">The command or executable to run.</param>
    /// <param name="arguments">The command-line arguments to pass to the process.</param>
    /// <param name="timeoutMilliseconds">The maximum time to wait for the process to complete before timing out.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A tuple containing the exit code, standard output, and standard error of the process.</returns>
    public static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(string command, string arguments, int timeoutMilliseconds = 30_000, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        if (process == null)
            return (-1, string.Empty, $"Failed to start process: {command} {arguments}");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var processTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(timeoutMilliseconds, cancellationToken);

        var timedOut = await Task.WhenAny(Task.WhenAll(outputTask, errorTask, processTask), timeoutTask) == timeoutTask;

        if (timedOut)
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore kill errors
            }

        var output = await outputTask;
        var error = await errorTask;

        Duplicati.Library.Logging.Log.WriteExplicitMessage(LOGTAG, "RunProcessAsync", "Command '{0} {1}' exited with code {2}. Output: {3}. Error: {4}", command, arguments, process.ExitCode, output, error);

        if (timedOut)
            return (-1, string.Empty, $"Process timed out after {timeoutMilliseconds} ms: {command} {arguments}. Output so far: {await outputTask}. Error so far: {await errorTask}");
        else
            return (process.ExitCode, output, error);
    }
}
