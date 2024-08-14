namespace DeviceId.Internal.CommandExecutors;

/// <summary>
/// An implementation of <see cref="ICommandExecutor"/> that uses /bin/bash to execute commands.
/// </summary>
internal class BashCommandExecutor : CommandExecutorBase
{
    /// <summary>
    /// Executes the specified command.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <returns>The command output.</returns>
    public override string Execute(string command)
    {
        return RunWithShell("/bin/bash", $"-c \"{command.Replace("\"", "\\\"")}\"").Trim('\r').Trim('\n').TrimEnd().TrimStart();
    }
}
