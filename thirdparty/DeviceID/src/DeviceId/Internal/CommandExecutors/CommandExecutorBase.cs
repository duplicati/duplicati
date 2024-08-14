using System.Diagnostics;

namespace DeviceId.Internal.CommandExecutors;

/// <summary>
/// A base implementation of <see cref="ICommandExecutor"/>.
/// </summary>
internal abstract class CommandExecutorBase : ICommandExecutor
{
    /// <summary>
    /// Executes the specified command.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <returns>The command output.</returns>
    public abstract string Execute(string command);

    /// <summary>
    /// Runs the specified command with the specified shell.
    /// </summary>
    /// <param name="shell">The shell to use.</param>
    /// <param name="command">The command to run.</param>
    /// <returns>The output.</returns>
    protected string RunWithShell(string shell, string command)
    {
        var psi = new ProcessStartInfo();
        psi.FileName = shell;
        psi.Arguments = command;
        psi.RedirectStandardOutput = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var process = Process.Start(psi);

        process?.WaitForExit();

        var output = process?.StandardOutput.ReadToEnd();

        return output;
    }
}
