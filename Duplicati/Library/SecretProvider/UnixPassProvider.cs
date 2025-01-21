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
/// Implementation of a secret provider that reads secrets from the Unix pass utility
/// </summary>
public class UnixPassProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Key => "pass";

    /// <inheritdoc />
    public string DisplayName => Strings.UnixPassProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.UnixPassProvider.Description;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
        => CommandLineArgumentMapper.MapArguments(new UnixPassProviderConfig())
            .ToList();

    /// <summary>
    /// The configuration for the secret provider; null if not initialized
    /// </summary>
    private UnixPassProviderConfig? _config;

    /// <summary>
    /// The configuration for the Unix pass provider
    /// </summary>
    private class UnixPassProviderConfig : ICommandLineArgumentMapper
    {
        /// <summary>
        /// The command to run to get a password
        /// </summary>
        public string PassCommand { get; set; } = "pass";

        /// <summary>
        /// Gets the command line argument description for a member
        /// </summary>
        /// <param name="name">The name of the member</param>
        /// <returns>The command line argument description, or null if the member is not a command line argument</returns>
        public static CommandLineArgumentDescriptionAttribute? GetCommandLineArgumentDescription(string name)
            => name switch
            {
                nameof(PassCommand) => new CommandLineArgumentDescriptionAttribute() { Name = "pass-command", Type = CommandLineArgument.ArgumentType.String, ShortDescription = Strings.UnixPassProvider.PassCommandDescriptionShort, LongDescription = Strings.UnixPassProvider.PassCommandDescriptionLong },
                _ => null
            };

        /// <inheritdoc />
        CommandLineArgumentDescriptionAttribute? ICommandLineArgumentMapper.GetCommandLineArgumentDescription(MemberInfo mi)
            => GetCommandLineArgumentDescription(mi.Name);
    }

    /// <inheritdoc />
    public Task InitializeAsync(System.Uri config, CancellationToken cancellationToken)
    {
        var args = HttpUtility.ParseQueryString(config.Query);
        _config = CommandLineArgumentMapper.ApplyArguments(new UnixPassProviderConfig(), args);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        if (_config is null)
            throw new InvalidOperationException("The UnixPassProvider has not been initialized");

        var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var value = await GetString(key, _config, cancellationToken).ConfigureAwait(false);
            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Gets a string from the pass utility
    /// </summary>
    /// <param name="name">The name of the secret</param>
    /// <param name="config">The configuration for the Unix pass provider</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The secret</returns>
    private static async Task<string> GetString(string name, UnixPassProviderConfig config, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(config.PassCommand)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("show");
        psi.ArgumentList.Add(name);

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start pass");

        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(error))
            throw new UserInformationException($"Error running pass: {error}", "PassError");
        if (process.ExitCode != 0)
            throw new UserInformationException($"pass failed with exit code {process.ExitCode}", "PassFailed");
        if (string.IsNullOrWhiteSpace(output))
            throw new UserInformationException("pass returned no output", "PassNoOutput");

        return output.Trim();
    }
}
