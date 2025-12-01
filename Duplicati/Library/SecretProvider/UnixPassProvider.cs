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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Web;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.SecretProvider;

/// <summary>
/// Implementation of a secret provider that reads secrets from the Unix pass utility
/// </summary>
[SupportedOSPlatform("linux")]
public class UnixPassProvider : ISecretProvider
{
    /// <summary>
    /// The default pass command
    /// </summary>
    private const string DefaultPassCommand = "pass";

    /// <inheritdoc />
    public string Key => "pass";

    /// <inheritdoc />
    public string DisplayName => Strings.UnixPassProvider.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.UnixPassProvider.Description;

    /// <summary>
    /// Cached support detection for the <c>pass</c> utility, using a PATH scan.
    /// This avoids re-scanning PATH multiple times across different instances.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<bool>> _passSupportCache = new();

    /// <inheritdoc />
    public bool IsSupported()
    {
        if (string.IsNullOrWhiteSpace(_config?.PassCommand) || _config.PassCommand.ContainsAny(Path.GetInvalidPathChars()) || _config.PassCommand.Contains(Path.PathSeparator))
            return false;

        return _passSupportCache.GetOrAdd(_config.PassCommand, pc => new Lazy<bool>(() => CheckPassSupport(pc))).Value;
    }

    /// <inheritdoc />
    public bool IsSetSupported => IsSupported();

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands
        => CommandLineArgumentMapper.MapArguments(new UnixPassProviderConfig())
            .ToList();

    /// <summary>
    /// The configuration for the secret provider; null if not initialized
    /// </summary>
    private UnixPassProviderConfig? _config;

    /// <summary>
    /// Checks whether the configured <c>pass</c> command is available by scanning PATH.
    /// This works across platforms (Linux, macOS, Windows) and avoids spawning a process.
    /// </summary>
    /// <param name="passCommand">The pass command to check.</param>
    /// <returns><c>true</c> if the command appears to be available; otherwise <c>false</c>.</returns>
    private static bool CheckPassSupport(string passCommand)
    {
        if (string.IsNullOrWhiteSpace(passCommand))
            return false;

        try
        {
            // If the command contains a directory separator, treat it as a path and just check for existence.
            if (passCommand.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
                return File.Exists(passCommand);

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv))
                return false;

            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var d = dir.Trim();
                if (string.IsNullOrEmpty(d))
                    continue;

                var candidate = Path.Combine(d, passCommand);
                if (File.Exists(candidate))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The configuration for the Unix pass provider
    /// </summary>
    private class UnixPassProviderConfig : ICommandLineArgumentMapper
    {
        /// <summary>
        /// The command to run to get a password
        /// </summary>
        public string PassCommand { get; set; } = DefaultPassCommand;

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

    /// <inheritdoc />
    public async Task SetSecretAsync(string key, string value, bool overwrite, CancellationToken cancellationToken)
    {
        if (_config is null)
            throw new InvalidOperationException("The UnixPassProvider has not been initialized");

        if (!overwrite && await SecretExistsAsync(key, _config, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"The key '{key}' already exists");

        await InsertSecretAsync(key, value, _config, overwrite, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Checks whether a secret exists in the pass utility
    /// </summary>
    /// <param name="name">The name of the secret</param>
    /// <param name="config">The configuration for the Unix pass provider</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>True if the secret exists; false otherwise</returns>
    private static async Task<bool> SecretExistsAsync(string name, UnixPassProviderConfig config, CancellationToken cancellationToken)
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);

        return process.ExitCode == 0;
    }

    /// <summary>
    /// Inserts a secret into the pass utility
    /// </summary>
    /// <param name="name">The name of the secret</param>
    /// <param name="value">The value of the secret</param>
    /// <param name="config">The configuration for the Unix pass provider</param>
    /// <param name="overwrite">Whether to overwrite an existing secret</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    private static async Task InsertSecretAsync(string name, string value, UnixPassProviderConfig config, bool overwrite, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(config.PassCommand)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("insert");
        if (overwrite)
            psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("--multiline");
        psi.ArgumentList.Add(name);

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start pass");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.StandardInput.WriteAsync(value.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.WriteAsync(Environment.NewLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();

        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = await stderrTask.ConfigureAwait(false);
            throw new UserInformationException($"pass failed with exit code {process.ExitCode}: {error}", "PassInsertFailed");
        }
    }
}
