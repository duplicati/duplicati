using System.CommandLine.Binding;
using System.Dynamic;
using Duplicati.Library.Backend;
using Newtonsoft.Json;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Manages console output, optionally serializing it to JSON.
/// </summary>
/// <remarks>
/// This class captures command execution details, messages, and exceptions, providing flexibility to either output them to the console
/// or serialize them into a JSON format based on the <paramref name="jsonOutput"/> parameter.
/// </remarks>
public sealed class OutputInterceptor(bool jsonOutput, BindingContext bindingContext)
{
    private readonly DateTimeOffset _timestamp = DateTimeOffset.Now;
    private readonly List<string> _commandMessages = [];
    private readonly List<string> _exceptionMessages = [];
    private string? _command;
    private bool _success;
    private readonly Dictionary<string, object?> _extendedProperties = [];
    public bool JsonOutputMode { get; } = jsonOutput;
    public int ExitCode { get; set; }

    /// <summary>
    /// Gets the binding context associated with this interceptor.
    /// </summary>
    public BindingContext BindingContext { get; } = bindingContext ?? throw new ArgumentNullException(nameof(bindingContext));

    /// <summary>
    /// Sets the command string to be intercepted and tracked.
    /// </summary>
    /// <param name="command">The command string to set. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command"/> is null.</exception>
    public void SetCommand(string command)
    {
        _command = command ?? throw new ArgumentNullException(nameof(command));
    }

    /// <summary>
    /// Sets the result of the command execution.
    /// </summary>
    /// <param name="success">A value indicating whether the ** business rule ** was successful. On exception by definition it will be false.</param>
    public void SetResult(bool success)
    {
        _success = success;
    }

    /// <summary>
    /// Appends an exception message to the interceptor.
    /// </summary>
    /// <param name="message">The exception message to append. Ignored if null or empty.</param>
    /// <remarks>
    /// If JSON output is enabled, the message is stored in a list; otherwise, it is written to the console.
    /// </remarks>
    public void AppendExceptionMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (JsonOutputMode)
        {
            _exceptionMessages.Add(message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }
    
    public void AppendCustomObject(string keyName, object? customObject)
    {
        _extendedProperties.Add(keyName, customObject);
    }

    /// <summary>
    /// Appends a console message to the interceptor.
    /// </summary>
    /// <param name="message">The console message to append. Ignored if null or empty.</param>
    /// <remarks>
    /// If JSON output is enabled, the message is stored in a list; otherwise, it is written to the console.
    /// </remarks>
    public void AppendConsoleMessage(string? message)
    {
        if (message == null) return;

        if (JsonOutputMode && !string.IsNullOrEmpty(message))
            _commandMessages.Add(message);
        else
            Console.WriteLine(message);
    }

    /// <summary>
    /// Serializes the intercepted data into a JSON string if JSON output is enabled.
    /// </summary>
    /// <returns>
    /// A JSON string containing the intercepted data, or <c>null</c> if JSON output is disabled.
    /// </returns>
    /// <remarks>
    /// The serialized result includes the timestamp, command, success status, messages, and exceptions in a structured format.
    /// </remarks>
    public string? GetSerializedResult()
    {
        if (!JsonOutputMode) return null;
        
        dynamic result = new ExpandoObject();
        result.Timestamp = _timestamp.ToString("O");
        result.UnixTimestamp = _timestamp.ToUnixTimeSeconds();
        result.Command = _command;
        result.Success = _success;
        result.ExitCode = ExitCode;
        result.Messages = _commandMessages.AsReadOnly();
        result.Exceptions = _exceptionMessages.AsReadOnly();
        foreach (var kvp in _extendedProperties) ((IDictionary<string, object>)result)[kvp.Key] = kvp.Value ?? string.Empty;

        return JsonConvert.SerializeObject(result, Formatting.Indented);
    }
}