namespace DeviceId.Internal.CommandExecutors;

/// <summary>
/// Enumerate the various command executors that are available.
/// </summary>
internal static class CommandExecutor
{
    /// <summary>
    /// Gets a command executor that uses /bin/bash to execute commands.
    /// </summary>
    public static ICommandExecutor Bash { get; } = new BashCommandExecutor();
}
