// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
using System.CommandLine;
using System.CommandLine.Binding;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Main;

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
    public static readonly Option<DirectoryInfo?> serverDatafolderOption = new Option<DirectoryInfo?>($"--{DataFolderManager.SERVER_DATAFOLDER_OPTION}", description: "The server datafolder to use for locating the database and storing configuration", getDefaultValue: () => new DirectoryInfo(DataFolderManager.DATAFOLDER));
    /// <summary>
    /// The server portable mode option.
    /// </summary>
    public static readonly Option<bool> portableModeOption = new Option<bool>($"--{DataFolderManager.PORTABLE_MODE_OPTION}", description: "Use portable mode for locating the database and storing configuration", getDefaultValue: () => DataFolderManager.PORTABLE_MODE);
    /// <summary>
    /// The settings file option.
    /// </summary>
    public static readonly Option<FileInfo?> settingsFileOption = new Option<FileInfo?>("--settings-file", description: "The settings file to use", getDefaultValue: () => null);

    /// <summary>
    /// The settings file option.
    /// </summary>
    public static readonly Option<bool> insecureOption = new Option<bool>("--insecure", description: "Accepts any TLS/SSL certificate (dangerous)", getDefaultValue: () => false);

    /// <summary>
    /// The settings encryption key option.
    /// </summary>
    public static readonly Option<string?> settingsEncryptionKeyOption = new Option<string?>("--settings-encryption-key", description: $"The encryption key to use for the settings file. Can also be supplied with environment variable {Library.Encryption.EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME}", getDefaultValue: () => null);

    /// <summary>
    /// The secret provider option.
    /// </summary>
    public static readonly Option<string?> secretProviderOption = new Option<string?>("--secret-provider", description: "The secret provider to use for reading secrets", getDefaultValue: () => null);

    /// <summary>
    /// The secret provider cache option.
    /// </summary>
    public static readonly Option<SecretProviderHelper.CachingLevel> secretProviderCacheOption = new Option<SecretProviderHelper.CachingLevel>("--secret-provider-cache", description: "The secret provider cache to use for reading secrets", getDefaultValue: () => SecretProviderHelper.CachingLevel.None);

    /// <summary>
    /// The secret provider pattern option.
    /// </summary>
    public static readonly Option<string?> secretProviderPatternOption = new Option<string?>("--secret-provider-pattern", description: "The pattern to use for the secret provider", getDefaultValue: () => SecretProviderHelper.DEFAULT_PATTERN);
    /// The accepted host certificate option.
    /// </summary>
    public static readonly Option<string> acceptedHostCertificateOption = new Option<string>("--host-cert", description: "The SHA1 hash of the host certificate to accept. Use * for any certificate, same as --insecure (dangerous)", getDefaultValue: () => string.Empty);

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
        rootCommand.AddGlobalOption(portableModeOption);
        rootCommand.AddGlobalOption(settingsFileOption);
        rootCommand.AddGlobalOption(insecureOption);
        rootCommand.AddGlobalOption(settingsEncryptionKeyOption);
        rootCommand.AddGlobalOption(secretProviderOption);
        rootCommand.AddGlobalOption(secretProviderCacheOption);
        rootCommand.AddGlobalOption(secretProviderPatternOption);
        rootCommand.AddGlobalOption(acceptedHostCertificateOption);
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
            bindingContext.ParseResult.GetValueForOption(settingsFileOption)?.FullName ?? "settings.json",
            bindingContext.ParseResult.GetValueForOption(insecureOption),
            bindingContext.ParseResult.GetValueForOption(settingsEncryptionKeyOption) ?? Environment.GetEnvironmentVariable(Library.Encryption.EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME),
            bindingContext.ParseResult.GetValueForOption(secretProviderOption),
            bindingContext.ParseResult.GetValueForOption(secretProviderCacheOption),
            bindingContext.ParseResult.GetValueForOption(secretProviderPatternOption) ?? SecretProviderHelper.DEFAULT_PATTERN,
            bindingContext.ParseResult.GetValueForOption(acceptedHostCertificateOption)
        );

    /// <summary>
    /// Gets the bound value.
    /// </summary>
    /// <param name="bindingContext">The binding context to get the value from.</param>
    /// <returns>The bound value.</returns>
    protected override Settings GetBoundValue(BindingContext bindingContext) =>
        GetSettings(bindingContext);

}
