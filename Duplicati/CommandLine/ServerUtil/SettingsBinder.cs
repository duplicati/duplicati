using System.CommandLine;
using System.CommandLine.Binding;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Binds settings from command line options.
/// </summary>
public class SettingsBinder : BinderBase<Settings>
{
    /// <summary>
    /// The password option.
    /// </summary>
    public static readonly Option<string?> passwordOption = new Option<string?>("--password", description: "The password to use for connecting to the server", getDefaultValue: () => null);
    /// <summary>
    /// The host URL option.
    /// </summary>
    public static readonly Option<Uri> hostUrlOption = new Option<Uri>("--hosturl", description: "The host URL to use", getDefaultValue: () => new Uri("http://localhost:8200"));
    /// <summary>
    /// The server datafolder option.
    /// </summary>
    public static readonly Option<DirectoryInfo?> serverDatafolderOption = new Option<DirectoryInfo?>("--server-datafolder", description: "The server datafolder to use for locating the database", getDefaultValue: () => null);
    /// <summary>
    /// The settings file option.
    /// </summary>
    public static readonly Option<FileInfo?> settingsFileOption = new Option<FileInfo?>("--settings-file", description: "The settings file to use", getDefaultValue: () => null);

    /// <summary>
    /// The settings file option.
    /// </summary>
    public static readonly Option<bool> insecureOption = new Option<bool>("--insecure", description: "Accepts any TLS/SSL certificate (dangerous)", getDefaultValue: () => false);

    /// <summary>
    /// Adds global options to the root command.
    /// </summary>
    /// <param name="rootCommand">The root command to add the options to.</param>
    /// <returns>The root command with the options added.</returns>
    public static RootCommand AddGlobalOptions(RootCommand rootCommand)
    {
        rootCommand.AddGlobalOption(passwordOption);
        rootCommand.AddGlobalOption(hostUrlOption);
        rootCommand.AddGlobalOption(serverDatafolderOption);
        rootCommand.AddGlobalOption(settingsFileOption);
        rootCommand.AddGlobalOption(insecureOption);
        return rootCommand;
    }

    /// <summary>
    /// Gets the settings instance from the binding context.
    /// </summary>
    /// <param name="bindingContext">The binding context to get the settings from.</param>
    /// <returns>The settings instance.</returns>
    public static Settings GetSettings(BindingContext bindingContext) =>
        Settings.Load(
            bindingContext.ParseResult.GetValueForOption(passwordOption),
            bindingContext.ParseResult.GetValueForOption(hostUrlOption),
            bindingContext.ParseResult.GetValueForOption(serverDatafolderOption)?.FullName,
            bindingContext.ParseResult.GetValueForOption(settingsFileOption)?.FullName ?? "settings.json",
            bindingContext.ParseResult.GetValueForOption(insecureOption)
        );

    /// <summary>
    /// Gets the bound value.
    /// </summary>
    /// <param name="bindingContext">The binding context to get the value from.</param>
    /// <returns>The bound value.</returns>
    protected override Settings GetBoundValue(BindingContext bindingContext) =>
        GetSettings(bindingContext);

}
