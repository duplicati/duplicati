using System.CommandLine.Binding;
using System.CommandLine.Parsing;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// An abstract binder class for managing a singleton instance of <see cref="OutputInterceptor"/>.
/// </summary>
/// <remarks>
/// This class ensures that only one instance of <see cref="OutputInterceptor"/> is associated with a given <see cref="BindingContext"/>.
/// </remarks>
public abstract class OutputInterceptorBinder : BinderBase<OutputInterceptor>
{
    private static OutputInterceptor? _instance;

    /// <summary>
    /// Gets the current instance of <see cref="OutputInterceptor"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the instance has not been initialized.</exception>
    public static OutputInterceptor? Instance => _instance;

    /// <summary>
    /// Retrieves or creates a <see cref="OutputInterceptor"/> instance for the specified binding context.
    /// </summary>
    /// <param name="bindingContext">The binding context to associate with the interceptor. Must not be null.</param>
    /// <returns>The existing or newly created <see cref="OutputInterceptor"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bindingContext"/> is null.</exception>
    public static OutputInterceptor GetConsoleInterceptor(BindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext, nameof(bindingContext));

        if (_instance is not null && ReferenceEquals(_instance.BindingContext, bindingContext))
        {
            return _instance;
        }

        _instance = CreateInterceptor(bindingContext);
        return _instance;
    }

    /// <summary>
    /// Gets the bound <see cref="OutputInterceptor"/> value for the specified binding context.
    /// </summary>
    /// <param name="bindingContext">The binding context to retrieve the interceptor for.</param>
    /// <returns>The associated <see cref="OutputInterceptor"/> instance.</returns>
    protected override OutputInterceptor GetBoundValue(BindingContext bindingContext)
    {
        return GetConsoleInterceptor(bindingContext);
    }

    /// <summary>
    /// Creates a new <see cref="OutputInterceptor"/> instance with the specified binding context.
    /// </summary>
    /// <param name="bindingContext">The binding context to initialize the interceptor with.</param>
    /// <returns>A new <see cref="OutputInterceptor"/> instance.</returns>
    private static OutputInterceptor CreateInterceptor(BindingContext bindingContext)
    {
        var interceptor = new OutputInterceptor(bindingContext.ParseResult.Tokens.Any(x => x is { Type: TokenType.Option, Value: "--json" }), bindingContext);
        interceptor.SetCommand(bindingContext.ParseResult.CommandResult.Command.Name);
        return interceptor;
    }
}