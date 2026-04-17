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
using System.CommandLine.Builder;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.Reflection;
using Duplicati.CommandLine.ConfigureTool.Commands;
using Duplicati.Library.Interface;

namespace Duplicati.CommandLine.ConfigureTool;

/// <summary>
/// The main entry point for the ConfigureTool application.
/// </summary>
public static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    public static Task<int> Main(string[] args)
    {
        Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref args, Library.AutoUpdater.PackageHelper.NamedExecutable.ConfigureTool);

        var httpsCmd = new Command("https", "Manage HTTPS certificates for the Duplicati server")
        {
            HttpsCommand.CreateGenerateCommand(),
            HttpsCommand.CreateRenewCommand(),
            HttpsCommand.CreateRegenerateCaCommand(),
            HttpsCommand.CreateRemoveCommand(),
            HttpsCommand.CreateShowCommand(),
            HttpsCommand.CreateExportCommand(),
            HttpsCommand.CreateExportCaCommand()
        };

        var rootCmd = new RootCommand("Duplicati Configure Tool")
        {
            httpsCmd
        };

        return new CommandLineBuilder(rootCmd)
            .UseDefaults()
            .UseExceptionHandler((ex, context) =>
            {
                var rex = ex is TargetInvocationException tie
                    ? tie.InnerException
                    : ex;
                    
                if (rex is UserInformationException userInformationException)
                {
                    Console.WriteLine("ErrorID: {0}", userInformationException.HelpID);
                    Console.WriteLine("Message: {0}", userInformationException.Message);
                    context.ExitCode = 2;
                }
                else
                {
                    Console.WriteLine("Exception: " + rex);
                    context.ExitCode = 1;
                }
            })
            .Build()
            .InvokeAsync(args);
    }
}
