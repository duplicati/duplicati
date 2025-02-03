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
        testcmd.Handler = CommandHandler.Create((string secretUrl, string[] secrets) => RunTest(secretUrl, secrets));

        var infocmd = new Command("info", "Displays information about the secret provider")
        {
            new Argument<string>("secret-url", description: "The connection string to the secret provider, or just the leading part of the URL")
        };
        infocmd.Handler = CommandHandler.Create((string secretUrl) => ShowInfo(secretUrl));

        var cmd = new RootCommand("Duplicati Secret Tool")
        {
            testcmd,
            infocmd
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

    private static Task<int> ShowInfo(string secretUrl)
    {
        var p = secretUrl.IndexOf(':');
        var key = p >= 0 ? secretUrl.Substring(0, p) : secretUrl;

        try
        {
            var metadata = SecretProviderLoader.GetProviderMetadata(key);
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
        catch (Exception ex)
        {
            Console.WriteLine($"No information found for secret provider '{key}': {ex.Message}");
            return Task.FromResult(1);
        }

        return Task.FromResult(0);
    }
}

