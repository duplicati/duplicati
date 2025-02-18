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

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class RunBackup
{
    public static Command Create() =>
        new Command("run", "Runs a backup")
        {
            new Argument<string>("backup", "The backup to run, either ID or exact name (case-insensitive)") {
                Arity = ArgumentArity.ExactlyOne
            },
            new Option<bool>("--wait", "Wait for the backup to finish before returning") {
                IsRequired = false
            },
            new Option<int>("--poll-interval", description: "The interval in seconds to poll for backup status", getDefaultValue: () => 5) {
                IsRequired = false,
            },
            new Option<bool>("--quiet", "Do not print progress messages") {
                IsRequired = false
            }
        }
        .WithHandler(CommandHandler.Create<Settings, string, bool, int, bool>(async (settings, backup, wait, pollinterval, quiet) =>
        {
            if (pollinterval < 1)
                throw new UserReportedException("Poll interval must be at least 1 second");

            var connection = await settings.GetConnection();

            var matchingBackup = (await connection.ListBackups())
                .FirstOrDefault(b => string.Equals(b.Name, backup, StringComparison.OrdinalIgnoreCase) || string.Equals(b.ID, backup));

            if (matchingBackup == null)
                throw new UserReportedException("No backup found with supplied ID or name");

            if (!quiet)
                Console.WriteLine($"Running backup {matchingBackup.Name} (ID: {matchingBackup.ID})");
            await connection.RunBackup(matchingBackup.ID);

            if (wait)
            {
                if (!quiet)
                    Console.WriteLine("Waiting for backup to finish...");
                await connection.WaitForBackup(matchingBackup.ID, TimeSpan.FromSeconds(pollinterval), (msg) =>
                {
                    if (!quiet)
                        Console.WriteLine($"[{DateTime.Now}]: {msg}");
                });

                if (!quiet)
                    Console.WriteLine("Backup finished");
            }
        }));
}
