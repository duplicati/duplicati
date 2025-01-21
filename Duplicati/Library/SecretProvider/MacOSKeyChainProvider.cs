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

using System.Diagnostics;
using System.Reflection;
using System.Web;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// Implementation of a secret provider that reads secrets from the MacOS keychain
/// </summary>
public class MacOSKeyChainProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Key => "keychain";

    /// <inheritdoc />
    public string DisplayName => Strings.MacOSKeyChainProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.MacOSKeyChainProvider.Description;

    /// <summary>
    /// The type of password to get
    /// </summary>
    private enum PasswordType
    {
        /// <summary>
        /// A generic password
        /// </summary>
        Generic,
        /// <summary>
        /// An internet password
        /// </summary>
        Internet
    }

    /// <summary>
    /// The settings for the keychain
    /// </summary>
    private class KeyChainSettings : ICommandLineArgumentMapper
    {
        /// <summary>
        /// The service name
        /// </summary>
        public string? Service { get; set; }
        /// <summary>
        /// The account name
        /// </summary>
        public string? Account { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use internet passwords as opposed to generic passwords
        /// </summary>
        public PasswordType Type { get; set; } = PasswordType.Generic;

        /// <summary>
        /// Gets the command line argument description for a member
        /// </summary>
        /// <param name="name">The name of the member</param>
        /// <returns>The command line argument description</returns>
        public static CommandLineArgumentDescriptionAttribute? GetCommandLineArgumentDescription(string name)
            => name switch
            {
                nameof(Service) => new CommandLineArgumentDescriptionAttribute() { Name = "service", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.MacOSKeyChainProvider.ServiceDescriptionShort, LongDescription = Strings.MacOSKeyChainProvider.ServiceDescriptionLong },
                nameof(Account) => new CommandLineArgumentDescriptionAttribute() { Name = "account", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.MacOSKeyChainProvider.AccountDescriptionShort, LongDescription = Strings.MacOSKeyChainProvider.AccountDescriptionLong },
                nameof(Type) => new CommandLineArgumentDescriptionAttribute() { Name = "type", Type = CommandLineArgument.ArgumentType.Enumeration, ShortDescription = Strings.MacOSKeyChainProvider.TypeDescriptionShort, LongDescription = Strings.MacOSKeyChainProvider.TypeDescriptionLong },
                _ => null
            };

        /// <inheritdoc />
        CommandLineArgumentDescriptionAttribute? ICommandLineArgumentMapper.GetCommandLineArgumentDescription(MemberInfo mi)
            => GetCommandLineArgumentDescription(mi.Name);
    }

    /// <summary>
    /// The settings for the keychain; null if not initialized
    /// </summary>
    private KeyChainSettings? _settings;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
        => CommandLineArgumentMapper.MapArguments(new KeyChainSettings())
            .ToList();


    /// <inheritdoc />
    public Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("The MacOSKeyChainProvider is only supported on MacOS");

        var args = HttpUtility.ParseQueryString(config.Query);
        _settings = CommandLineArgumentMapper.ApplyArguments(new KeyChainSettings(), args);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_settings is null)
            throw new InvalidOperationException("The MacOSKeyChainProvider has not been initialized");

        var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var value = await GetStringAsync(key, _settings, cancellationToken).ConfigureAwait(false);
            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Gets a string from the keychain using the security command
    /// </summary>
    /// <param name="name">The name of the key to get</param>
    /// <param name="settings">The settings for the keychain</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The value of the key</returns>
    private static async Task<string> GetStringAsync(string name, KeyChainSettings settings, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (settings.Type == PasswordType.Internet)
            psi.ArgumentList.Add("find-internet-password");
        else
            psi.ArgumentList.Add("find-generic-password");

        if (!string.IsNullOrWhiteSpace(settings.Service))
        {
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(settings.Service);
        }

        if (!string.IsNullOrWhiteSpace(settings.Account))
        {
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(settings.Account);
        }

        psi.ArgumentList.Add("-w");
        psi.ArgumentList.Add("-l");
        psi.ArgumentList.Add(name);

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start the security process");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new UserInformationException($"Item not found in keychain: {name}", "KeyChainItemMissing");

        output = output.Trim();
        if (string.IsNullOrWhiteSpace(output))
            throw new UserInformationException($"The key '{name}' returned an empty value", "KeyChainItemEmpty");

        if (output.IndexOfAny(['\r', '\n']) >= 0)
            throw new UserInformationException($"The key '{name}' returned a multi-line value", "KeyChainItemMultiLine");

        return output;
    }
}
