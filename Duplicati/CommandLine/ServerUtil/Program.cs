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
using System.Runtime.CompilerServices;
using Duplicati.CommandLine.ServerUtil.Commands;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Utility;

[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// The entry point of the application
/// </summary>
public static class Program
{
    /// <summary>
    /// Creates the root command with all subcommands and global options.
    /// </summary>
    /// <returns>The configured root command</returns>
    internal static RootCommand CreateRootCommand()
    {
        var rootCmd = new RootCommand("Server CLI tool for Duplicati")
            {
                Pause.Create(),
                Resume.Create(),
                DeleteBackup.Create(),
                ListBackups.Create(),
                RunBackup.Create(),
                Login.Create(),
                ChangePassword.Create(),
                Logout.Create(),
                Import.Create(),
                Export.Create(),
                Health.Create(),
                IssueForeverToken.Create(),
                ServerStatus.Create()
            };

        rootCmd = SettingsBinder.AddGlobalOptions(rootCmd);
        rootCmd.UseAdditionalHelpAliases();
        return rootCmd;
    }

    /// <summary>
    /// Invokes the builder
    /// </summary>
    /// <param name="args"></param>
    /// <returns>The return code</returns>
    public static async Task<int> MainAsync(string[] args)
    {
        PreloadSettingsLoader.ConfigurePreloadSettings(ref args, PackageHelper.NamedExecutable.ServerUtil);

        var rootCmd = CreateRootCommand();
        var parseResult = rootCmd.Parse(args);

        // Create the output interceptor up-front, mirroring the middleware that
        // ran before the command handlers in the previous System.CommandLine version.
        // If a command handler throws before it creates the interceptor itself
        // (e.g. while loading the settings), the catch block below can still
        // report the failure to the console instead of exiting silently.
        if (parseResult.Errors.Count == 0)
            OutputInterceptorBinder.GetConsoleInterceptor(parseResult);

        try
        {
            var exitCode = await parseResult.InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false
            });

            // Propagate a non-zero result from the command (e.g. a backup that
            // finished with warnings/errors) to the process exit code. The exception
            // path already sets the exit code directly and never reaches here.
            if (OutputInterceptorBinder.Instance is { ExitCode: not 0 } instance)
                exitCode = instance.ExitCode;

            var jsonResult = OutputInterceptorBinder.Instance?.GetSerializedResult();
            if (jsonResult != null)
                Console.WriteLine(jsonResult);

            return exitCode;
        }
        catch (Exception ex)
        {
            OutputInterceptorBinder.Instance?.SetResult(false);

            if (ex is UserReportedException ure)
            {
                OutputInterceptorBinder.Instance?.AppendExceptionMessage(ure.Message);
                if (OutputInterceptorBinder.Instance != null)
                    OutputInterceptorBinder.Instance.ExitCode = 2;
            }
            else
            {
                OutputInterceptorBinder.Instance?.AppendExceptionMessage(ex.ToString());
                if (OutputInterceptorBinder.Instance != null)
                    OutputInterceptorBinder.Instance.ExitCode = 1;
            }

            var jsonResult = OutputInterceptorBinder.Instance?.GetSerializedResult();
            if (jsonResult != null)
                Console.WriteLine(jsonResult);

            return OutputInterceptorBinder.Instance?.ExitCode ?? 1;
        }
    }
}
