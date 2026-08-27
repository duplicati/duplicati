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
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine.DatabaseTool;

/// <summary>
/// The entry point of the application
/// </summary>
public static class Program
{
    /// <summary>
    /// Executes the program
    /// </summary>
    /// <param name="args">The arguments</param>
    /// <returns>The return code</returns>
    public static Task<int> MainAsync(string[] args)
    {
        Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref args, Library.AutoUpdater.PackageHelper.NamedExecutable.ServerUtil);

        var rootCmd = new RootCommand("Database CLI tool for Duplicati")
            {
                Commands.Downgrade.Create(),
                Commands.Upgrade.Create(),
                Commands.List.Create(),
                Commands.Execute.Create(),
                Commands.Verify.Create(),
                Commands.Cleanup.Create(),
                Commands.WipeEncryption.Create(),
            };

        // Registered so the option is accepted and shown in help; the value is read directly
        // from the process arguments/environment by DataFolderManager/Util.
        rootCmd.Options.Add(new Option<bool>(
            $"--{DataFolderManager.ALLOW_INSECURE_DATAFOLDER_OPTION}")
        {
            Description = "Allow the data folder to have insecure permissions instead of rejecting it",
            DefaultValueFactory = _ => false
        });

        rootCmd.UseAdditionalHelpAliases();

        try
        {
            return rootCmd.Parse(args).InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false
            });
        }
        catch (UserInformationException uie)
        {
            Console.WriteLine(uie.Message);
            return Task.FromResult(2);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return Task.FromResult(1);
        }
    }
}
