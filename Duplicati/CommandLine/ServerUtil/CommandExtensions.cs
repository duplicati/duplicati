using System.CommandLine;
using System.CommandLine.Invocation;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Extensions for <see cref="Command"/>.
/// </summary>
public static class CommandExtensions
{
    /// <summary>
    /// Adds the missing WithHandler method to <see cref="Command"/>.
    /// </summary>
    /// <param name="command">The command to add the handler to.</param>
    /// <param name="handler">The handler to add.</param>
    /// <returns>The command with the handler added.</returns>
    public static Command WithHandler(this Command command, ICommandHandler handler)
    {
        command.Handler = handler;
        return command;
    }
}
