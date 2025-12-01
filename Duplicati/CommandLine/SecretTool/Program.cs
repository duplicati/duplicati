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
using System.CommandLine.Builder;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Uri = System.Uri;

namespace Duplicati.CommandLine.SecretTool;

/// <summary>
/// The main entry point for the SecretTool application.
/// </summary>
public static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    public static Task<int> Main(string[] args)
    {
        Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref args, Library.AutoUpdater.PackageHelper.NamedExecutable.SecretTool);

        var testcmd = new Command("test", "Tests the secret provider")
        {
            new Argument<string>("secret-url", description: "The connection string to the secret provider"),
            new Argument<string[]>("secrets", description: "The secrets to fetch") { Arity = ArgumentArity.OneOrMore }
        };
        testcmd.Handler = CommandHandler.Create(RunTest);

        var infocmd = new Command("info", "Displays information about the secret provider")
        {
            new Argument<string>("secret-url", description: "The connection string to the secret provider, or just the leading part of the URL") { Arity = ArgumentArity.ZeroOrOne }
        };
        infocmd.Handler = CommandHandler.Create(ShowInfo);

        var setcmd = new Command("set", "Stores a secret value in the configured provider")
        {
            new Argument<string>("secret-url", description: "The connection string to the secret provider"),
            new Argument<string>("key", description: "The secret key to store"),
            new Argument<string?>("value", description: "The secret value to store", getDefaultValue: () => null) { Arity = ArgumentArity.ZeroOrOne }
        };

        var overwriteOption = new Option<bool>("--overwrite", "Overwrite the secret if it already exists");
        setcmd.AddOption(overwriteOption);
        setcmd.Handler = CommandHandler.Create<string, string, string?, bool>(SetSecret);

        var cmd = new RootCommand("Duplicati Secret Tool")
        {
            testcmd,
            infocmd,
            setcmd
        };

        return new CommandLineBuilder(cmd)
            .UseDefaults()
            .UseExceptionHandler((ex, context) =>
            {
                if (ex is UserInformationException userInformationException)
                {
                    Console.WriteLine("ErrorID: {0}", userInformationException.HelpID);
                    Console.WriteLine("Message: {0}", userInformationException.Message);
                    context.ExitCode = 2;
                }
                else
                {
                    Console.WriteLine("Exception: " + ex);
                    context.ExitCode = 1;
                }
            })
            .UseAdditionalHelpAliases()
            .Build()
            .InvokeAsync(args);
    }

    /// <summary>
    /// Runs the test command, which tests fetching secrets from the specified secret provider.
    /// </summary>
    /// <param name="secretUrl">The secret provider URL.</param>
    /// <param name="secrets">The secrets to fetch.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> RunTest(string secretUrl, string[] secrets)
    {
        var secretProvider = SecretProviderLoader.CreateInstance(secretUrl);
        await secretProvider.InitializeAsync(new Uri(secretUrl), CancellationToken.None);
        var result = await secretProvider.ResolveSecretsAsync(secrets, CancellationToken.None);
        Console.WriteLine("NOTE: Secret values are not displayed for security reasons");

        Console.WriteLine("Secrets:");
        foreach (var secret in secrets)
            Console.WriteLine($"- {secret}: {(result.ContainsKey(secret) ? "Found!" : "Not found")}");

        return 0;
    }

    /// <summary>
    /// Shows information about the specified secret provider.
    /// </summary>
    /// <param name="secretUrl">The secret provider URL.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> ShowInfo(string? secretUrl)
    {
        string? key = null;
        Console.WriteLine($"Supported secret providers on {Library.AutoUpdater.UpdaterManager.OperatingSystemName}:");
        foreach (var k in SecretProviderLoader.Keys)
        {
            var metadata = await SecretProviderLoader.GetProviderMetadata(k, CancellationToken.None);
            Console.WriteLine($"  {k} - {metadata.DisplayName}{(metadata.IsSupported ? "" : " (not supported)")}");
        }

        Console.WriteLine();

        try
        {
            if (string.IsNullOrWhiteSpace(secretUrl))
            {
                var defaultProvider = await SecretProviderLoader.GetDefaultSecretProviderForOperatingSystem(CancellationToken.None);
                if (defaultProvider == null)
                    throw new UserInformationException("No working default secret provider found", "NoDefaultSecretProvider");

                key = defaultProvider.Key;
            }
            else
            {
                var p = secretUrl.IndexOf(':');
                key = p >= 0 ? secretUrl.Substring(0, p) : secretUrl;
            }
            var metadata = await SecretProviderLoader.GetProviderMetadata(key, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(secretUrl))
                Console.WriteLine($"Default secret provider is '{key}'");
            else
                Console.WriteLine($"Secret provider '{key}'");
            var lines = metadata.Description.Trim().Split(new char[] { '\r', '\n' }, StringSplitOptions.None);
            foreach (var line in lines)
                Console.WriteLine($"  {line.Trim()}");
            Console.WriteLine();

            if (metadata.SupportedCommands.Count > 0)
            {
                Console.WriteLine("Supported options:");
                foreach (var cmd in metadata.SupportedCommands)
                {
                    Console.WriteLine($"  {cmd.Name}: {cmd.ShortDescription}");
                    Console.WriteLine($"    Type: {cmd.Type}");
                    if (cmd.DefaultValue != null)
                        Console.WriteLine($"    Default: {cmd.DefaultValue}");
                    if (cmd.ValidValues != null)
                        Console.WriteLine($"    Valid values: {string.Join(", ", cmd.ValidValues)}");
                    Console.WriteLine($"    {cmd.LongDescription}");
                    Console.WriteLine();
                }
            }
        }
        catch (Exception ex) when (ex is not UserInformationException)
        {
            Console.WriteLine($"No information found for secret provider '{key}': {ex.Message}");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Sets a secret value in the specified secret provider.
    /// </summary>
    /// <param name="secretUrl">The secret provider URL.</param>
    /// <param name="key">The key of the secret.</param>
    /// <param name="value">The value of the secret.</param>
    /// <param name="overwrite">Whether to overwrite an existing secret.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> SetSecret(string secretUrl, string key, string? value, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            value = Utility.ReadSecretFromConsole("Enter secret value: ");
            var confirm = Utility.ReadSecretFromConsole("Confirm secret value: ");
            if (value != confirm)
                throw new UserInformationException("Secret values do not match", "SecretMismatch");
        }

        var secretProvider = SecretProviderLoader.CreateInstance(secretUrl);
        await secretProvider.InitializeAsync(new Uri(secretUrl), CancellationToken.None);
        await secretProvider.SetSecretAsync(key, value, overwrite, CancellationToken.None);

        // Verify that the secret was stored correctly
        var result = await secretProvider.ResolveSecretsAsync([key], CancellationToken.None);
        if (!result.ContainsKey(key) || result[key] != value)
            throw new UserInformationException("Failed to verify that the secret was stored correctly", "SecretVerificationFailed");
        Console.WriteLine($"Secret '{key}' stored.");

        return 0;
    }
}

