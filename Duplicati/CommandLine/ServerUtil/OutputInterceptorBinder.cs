using System.CommandLine;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Manages a singleton instance of <see cref="OutputInterceptor"/>.
/// </summary>
/// <remarks>
/// This class ensures that only one instance of <see cref="OutputInterceptor"/> is associated with a given <see cref="ParseResult"/>.
/// </remarks>
public static class OutputInterceptorBinder
{
    private static OutputInterceptor? _instance;

    /// <summary>
    /// Gets the current instance of <see cref="OutputInterceptor"/>.
    /// </summary>
    public static OutputInterceptor? Instance => _instance;

    /// <summary>
    /// Retrieves or creates a <see cref="OutputInterceptor"/> instance for the specified parse result.
    /// </summary>
    /// <param name="parseResult">The parse result to associate with the interceptor. Must not be null.</param>
    /// <returns>The existing or newly created <see cref="OutputInterceptor"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="parseResult"/> is null.</exception>
    public static OutputInterceptor GetConsoleInterceptor(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult, nameof(parseResult));

        if (_instance is not null && ReferenceEquals(_instance.ParseResult, parseResult))
        {
            return _instance;
        }

        _instance = CreateInterceptor(parseResult);
        return _instance;
    }

    /// <summary>
    /// Creates a new <see cref="OutputInterceptor"/> instance with the specified parse result.
    /// </summary>
    /// <param name="parseResult">The parse result to initialize the interceptor with.</param>
    /// <returns>A new <see cref="OutputInterceptor"/> instance.</returns>
    private static OutputInterceptor CreateInterceptor(ParseResult parseResult)
    {
        var interceptor = new OutputInterceptor(parseResult.GetValue(SettingsBinder.jsonOutputOption), parseResult);
        interceptor.SetCommand(parseResult.CommandResult.Command.Name);
        return interceptor;
    }
}
