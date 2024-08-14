using DeviceId.Internal.CommandExecutors;

namespace DeviceId.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that executes a command.
/// </summary>
public class CommandComponent : IDeviceIdComponent
{
    /// <summary>
    /// The command executed by the component.
    /// </summary>
    private readonly string _command;

    /// <summary>
    /// The command executor to use.
    /// </summary>
    private readonly ICommandExecutor _commandExecutor;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandComponent"/> class.
    /// </summary>
    /// <param name="command">The command executed by the component.</param>
    /// <param name="commandExecutor">The command executor.</param>
    internal CommandComponent(string command, ICommandExecutor commandExecutor)
    {
        _command = command;
        _commandExecutor = commandExecutor;
    }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        return _commandExecutor.Execute(_command);
    }
}
