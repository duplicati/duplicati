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
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.SourceTool.Commands;

/// <summary>
/// The list command
/// </summary>
public static class List
{
    /// <summary>
    /// Creates the list command
    /// </summary>
    /// <returns>The command</returns>
    public static Command Create() =>
        new Command("list", "Lists all paths on the remote")
        {
            new Argument<string>("url", "The source URL") {
                Arity = ArgumentArity.ExactlyOne
            },
            new Option<int>("--max-depth", description: "The maximum depth to list", getDefaultValue: () => 0),
        }
        .WithHandler(CommandHandler.Create<string, int>(async (url, maxdepth) =>
            {
                var token = new CancellationTokenSource().Token;
                var folders = 0L;
                var files = 0L;

                using var source = await Common.GetProvider(url);
                await Common.Visit(source, maxdepth, (entry, level) =>
                {
                    if (entry.IsFolder)
                        folders++;
                    else
                        files++;
                    if (entry.IsFolder && level > 0)
                        level--;
                    if (!entry.IsRootEntry)
                        Console.WriteLine($"{new string(' ', (level + 1) * 2)}{entry.Path}");
                    return Task.FromResult(true);
                }, token);

                Console.WriteLine($"Found {folders} folders and {files} files");
            })
        );

}
