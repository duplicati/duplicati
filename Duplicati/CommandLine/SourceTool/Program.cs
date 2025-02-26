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
using System.CommandLine.Parsing;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine.SourceTool;

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
    public static Task<int> Main(string[] args)
    {
        Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref args, Library.AutoUpdater.PackageHelper.NamedExecutable.ServerUtil);

        var rootCmd = new RootCommand("Source CLI tool for Duplicati")
            {
                Commands.List.Create(),
                Commands.Download.Create(),
            };

        return new CommandLineBuilder(rootCmd)
            .UseDefaults()
            .UseExceptionHandler((ex, context) =>
            {
                if (ex is UserInformationException uie)
                {
                    Console.WriteLine(uie.Message);
                    context.ExitCode = 2;
                }
                else
                {
                    Console.WriteLine(ex.ToString());
                    context.ExitCode = 1;
                }
            })
            .UseAdditionalHelpAliases()
            .Build()
            .InvokeAsync(args);
    }
}
