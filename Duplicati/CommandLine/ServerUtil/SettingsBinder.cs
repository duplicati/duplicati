// Copyright (C) 2026, The Duplicati Team
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
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Encryption;
using Duplicati.Library.Main;
using Utility = Duplicati.Library.Utility.Utility;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Binds settings from command line options.
/// </summary>
public static class SettingsBinder
{
    /// <summary>
    /// The password option.
    /// </summary>
    public static readonly Option<string?> passwordOption = new Option<string?>("--password")
    {
        Description = "The password to use for connecting to the server",
        DefaultValueFactory = _ => null
    };
    /// <summary>
    /// The host URL option.
    /// </summary>
    public static readonly Option<Uri> hostUrlOption = new Option<Uri>("--hosturl")
    {
        Description = "The host URL to use",
        DefaultValueFactory = _ => new Uri($"http://{Utility.IpVersionCompatibleLoopback}:8200")
    };
    /// <summary>
    /// The server datafolder option.
    /// </summary>
    public static readonly Option<DirectoryInfo?> serverDatafolderOption = new Option<DirectoryInfo?>($"--{DataFolderManager.SERVER_DATAFOLDER_OPTION}")
    {
        Description = "The server datafolder to use for locating the database and storing configuration",
        DefaultValueFactory = _ => new DirectoryInfo(DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly))
    };
    /// <summary>
    /// The server portable mode option.
    /// </summary>
    public static readonly Option<bool> portableModeOption = new Option<bool>($"--{DataFolderManager.PORTABLE_MODE_OPTION}")
    {
        Description = "Use portable mode for locating the database and storing configuration",
        DefaultValueFactory = _ => DataFolderManager.PORTABLE_MODE
    };
    /// <summary>
    /// The allow-insecure-datafolder option.
    /// </summary>
    public static readonly Option<bool> allowInsecureDatafolderOption = new Option<bool>($"--{DataFolderManager.ALLOW_INSECURE_DATAFOLDER_OPTION}")
    {
        Description = "Allow the data folder to have insecure permissions instead of rejecting it",
        DefaultValueFactory = _ => false
    };
    /// <summary>
    /// The settings file option.
    /// </summary>
    public static readonly Option<FileInfo?> settingsFileOption = new Option<FileInfo?>("--settings-file")
    {
        Description = "The settings file to use",
        DefaultValueFactory = _ => null
    };

    /// <summary>
    /// The settings file option.
    /// </summary>
    public static readonly Option<bool> insecureOption = new Option<bool>("--insecure")
    {
        Description = "Accepts any TLS/SSL certificate (dangerous)",
        DefaultValueFactory = _ => false
    };

    /// <summary>
    /// The settings encryption key option.
    /// </summary>
    public static readonly Option<string?> settingsEncryptionKeyOption = new Option<string?>("--settings-encryption-key")
    {
        Description = $"The encryption key to use for the settings file. Can also be supplied with environment variable {EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME}",
        DefaultValueFactory = _ => null
    };

    /// <summary>
    /// The secret provider option.
    /// </summary>
    public static readonly Option<string?> secretProviderOption = new Option<string?>("--secret-provider")
    {
        Description = "The secret provider to use for reading secrets",
        DefaultValueFactory = _ => null
    };

    /// <summary>
    /// The secret provider cache option.
    /// </summary>
    public static readonly Option<SecretProviderHelper.CachingLevel> secretProviderCacheOption = new Option<SecretProviderHelper.CachingLevel>("--secret-provider-cache")
    {
        Description = "The secret provider cache to use for reading secrets",
        DefaultValueFactory = _ => SecretProviderHelper.CachingLevel.None
    };

    /// <summary>
    /// The secret provider pattern option.
    /// </summary>
    public static readonly Option<string?> secretProviderPatternOption = new Option<string?>("--secret-provider-pattern")
    {
        Description = "The pattern to use for the secret provider",
        DefaultValueFactory = _ => SecretProviderHelper.DEFAULT_PATTERN
    };
    /// The accepted host certificate option.
    /// </summary>
    public static readonly Option<string> acceptedHostCertificateOption = new Option<string>("--host-cert")
    {
        Description = "The SHA1 hash of the host certificate to accept. Use * for any certificate, same as --insecure (dangerous)",
        DefaultValueFactory = _ => string.Empty
    };

    /// <summary>
    /// The ignore revocation failure option.
    /// </summary>
    public static readonly Option<bool> ignoreRevocationFailureOption = new Option<bool>("--ignore-revocation-failure")
    {
        Description = "Ignore certificate revocation check failures, such as when the revocation server is offline or the status is unknown",
        DefaultValueFactory = _ => false
    };

    /// <summary>
    /// Option to wrap stdout as a json.
    /// </summary>
    public static readonly Option<bool> jsonOutputOption = new Option<bool>("--json")
    {
        Description = "Wraps stdout as a json",
        DefaultValueFactory = _ => false
    };

    /// <summary>
    /// Adds global options to the root command.
    /// </summary>
    /// <param name="rootCommand">The root command to add the options to.</param>
    /// <returns>The root command with the options added.</returns>
    public static RootCommand AddGlobalOptions(RootCommand rootCommand)
    {
        rootCommand.Options.Add(passwordOption);
        rootCommand.Options.Add(hostUrlOption);
        rootCommand.Options.Add(serverDatafolderOption);
        rootCommand.Options.Add(portableModeOption);
        rootCommand.Options.Add(allowInsecureDatafolderOption);
        rootCommand.Options.Add(settingsFileOption);
        rootCommand.Options.Add(insecureOption);
        rootCommand.Options.Add(settingsEncryptionKeyOption);
        rootCommand.Options.Add(secretProviderOption);
        rootCommand.Options.Add(secretProviderCacheOption);
        rootCommand.Options.Add(secretProviderPatternOption);
        rootCommand.Options.Add(acceptedHostCertificateOption);
        rootCommand.Options.Add(ignoreRevocationFailureOption);
        rootCommand.Options.Add(jsonOutputOption);
        return rootCommand;
    }

    /// <summary>
    /// Gets the settings instance from the parse result.
    /// </summary>
    /// <param name="parseResult">The parse result to get the settings from.</param>
    /// <returns>The settings instance.</returns>
    public static Settings GetSettings(ParseResult parseResult) =>
        Settings.Load(
            parseResult.GetValue(passwordOption),
            parseResult.GetValue(hostUrlOption)!,
            parseResult.GetValue(settingsFileOption)?.FullName ?? "settings.json",
            parseResult.GetValue(insecureOption),
            parseResult.GetValue(settingsEncryptionKeyOption) ?? Environment.GetEnvironmentVariable(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME),
            parseResult.GetValue(secretProviderOption),
            parseResult.GetValue(secretProviderCacheOption),
            parseResult.GetValue(secretProviderPatternOption) ?? SecretProviderHelper.DEFAULT_PATTERN,
            parseResult.GetValue(acceptedHostCertificateOption),
            parseResult.GetValue(ignoreRevocationFailureOption)
        );
}
