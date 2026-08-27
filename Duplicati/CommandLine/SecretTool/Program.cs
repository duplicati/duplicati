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
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine.SecretTool;

/// <summary>
/// The main entry point for the SecretTool application.
/// </summary>
public static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref args, Library.AutoUpdater.PackageHelper.NamedExecutable.SecretTool);

        var testSecretUrlArg = new Argument<string>("secret-url") { Description = "The connection string to the secret provider" };
        var testSecretsArg = new Argument<string[]>("secrets") { Description = "The secrets to fetch", Arity = ArgumentArity.OneOrMore };
        var testcmd = new Command("test", "Tests the secret provider")
        {
            testSecretUrlArg,
            testSecretsArg
        };
        testcmd.SetAction((parseResult, ct) => RunTest(
            parseResult.GetValue(testSecretUrlArg)!,
            parseResult.GetValue(testSecretsArg)!));

        var infoSecretUrlArg = new Argument<string?>("secret-url") { Description = "The connection string to the secret provider, or just the leading part of the URL", Arity = ArgumentArity.ZeroOrOne };
        var infocmd = new Command("info", "Displays information about the secret provider")
        {
            infoSecretUrlArg
        };
        infocmd.SetAction((parseResult, ct) => ShowInfo(
            parseResult.GetValue(infoSecretUrlArg)));

        var setSecretUrlArg = new Argument<string>("secret-url") { Description = "The connection string to the secret provider" };
        var setKeyArg = new Argument<string>("key") { Description = "The secret key to store" };
        var setValueArg = new Argument<string?>("value") { Description = "The secret value to store", DefaultValueFactory = _ => null, Arity = ArgumentArity.ZeroOrOne };
        var setcmd = new Command("set", "Stores a secret value in the configured provider")
        {
            setSecretUrlArg,
            setKeyArg,
            setValueArg
        };

        var overwriteOption = new Option<bool>("--overwrite") { Description = "Overwrite the secret if it already exists" };
        setcmd.Options.Add(overwriteOption);
        setcmd.SetAction((parseResult, ct) => SetSecret(
            parseResult.GetValue(setSecretUrlArg)!,
            parseResult.GetValue(setKeyArg)!,
            parseResult.GetValue(setValueArg),
            parseResult.GetValue(overwriteOption)));

        var cmd = new RootCommand("Duplicati Secret Tool")
        {
            testcmd,
            infocmd,
            setcmd
        };

        cmd.UseAdditionalHelpAliases();

        try
        {
            return await cmd.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
        }
        catch (UserInformationException userInformationException)
        {
            Console.WriteLine("ErrorID: {0}", userInformationException.HelpID);
            Console.WriteLine("Message: {0}", userInformationException.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception: " + ex);
            return 1;
        }
    }

    /// <summary>
    /// Runs the test command, which tests fetching secrets from the specified secret provider.
    /// </summary>
    /// <param name="secretUrl">The secret provider URL.</param>
    /// <param name="secrets">The secrets to fetch.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> RunTest(string secretUrl, string[] secrets)
    {
        var secretProvider = await SecretProviderLoader.CreateInstanceAsync(secretUrl, true, CancellationToken.None).ConfigureAwait(false);
        var result = await secretProvider.ResolveSecretsAsync(secrets, CancellationToken.None).ConfigureAwait(false);
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

        if (string.IsNullOrWhiteSpace(secretUrl))
        {
            Console.WriteLine($"Supported secret providers on {Library.AutoUpdater.UpdaterManager.OperatingSystemName}:");
            foreach (var k in SecretProviderLoader.Keys)
            {
                var metadata = await SecretProviderLoader.GetProviderMetadata(k, CancellationToken.None);
                Console.WriteLine($"  {k} - {metadata.DisplayName}{(metadata.IsSupported ? "" : " (not supported)")}");
            }

            Console.WriteLine();
        }


        try
        {
            if (string.IsNullOrWhiteSpace(secretUrl))
            {
                var defaultProvider = await SecretProviderLoader.GetDefaultSecretProviderForOperatingSystem(true, CancellationToken.None);
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

        var secretProvider = await SecretProviderLoader.CreateInstanceAsync(secretUrl, true, CancellationToken.None).ConfigureAwait(false);
        await secretProvider.SetSecretAsync(key, value, overwrite, CancellationToken.None);

        // Verify that the secret was stored correctly
        var result = await secretProvider.ResolveSecretsAsync([key], CancellationToken.None).ConfigureAwait(false);
        if (!result.ContainsKey(key) || result[key] != value)
            throw new UserInformationException("Failed to verify that the secret was stored correctly", "SecretVerificationFailed");
        Console.WriteLine($"Secret '{key}' stored.");

        return 0;
    }
}

