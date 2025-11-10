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
            new Option<bool>("--skip-queue", "Insert the backup as the first task in the queue, instead of putting it at the end") {
                IsRequired = false
            },
            new Option<int>("--poll-interval", description: "The interval in seconds to poll for backup status", getDefaultValue: () => 5) {
                IsRequired = false
            },
            new Option<bool>("--quiet", "Do not print progress messages") {
                IsRequired = false
            }
        }
        .WithHandler(CommandHandler.Create<Settings, OutputInterceptor, string, bool, bool, int, bool>(async (settings, output, backup, wait, skipqueue, pollinterval, quiet) =>
        {
            if (pollinterval < 1)
                throw new UserReportedException("Poll interval must be at least 1 second");

            var connection = await settings.GetConnection(output);

            var matchingBackup = (await connection.ListBackups())
                .FirstOrDefault(b => string.Equals(b.Name, backup, StringComparison.OrdinalIgnoreCase) || string.Equals(b.ID, backup));

            if (matchingBackup == null)
                throw new UserReportedException("No backup found with supplied ID or name");

            if (!quiet)
                output.AppendConsoleMessage($"Running backup {matchingBackup.Name} (ID: {matchingBackup.ID})");

            await connection.RunBackup(matchingBackup.ID, skipqueue);

            if (wait)
            {
                if (!quiet)
                    output.AppendConsoleMessage("Waiting for backup to finish...");

                await WaitForBackupWithRetries(connection, settings, matchingBackup.ID, pollinterval, output, msg =>
                {
                    if (!quiet)
                        output.AppendConsoleMessage($"[{DateTime.Now}]: {msg}");
                });
                var response = await connection.GetBackupStatus(matchingBackup.ID);
                if (!quiet)
                    output.AppendConsoleMessage("Backup status: " + response);
            }
            output.SetResult(true);
        }));

    /// <summary>
    /// Waits for a backup to finish with retries
    /// </summary>
    /// <param name="connection">The connection to use</param>
    /// <param name="settings">The settings to use</param>
    /// <param name="backupId">The ID of the backup to wait for</param>
    /// <param name="pollInterval">The interval in seconds to poll for backup status</param>
    /// <param name="output">The output interceptor to use</param>
    /// <param name="onPoll">The action to perform on each poll</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    private static async Task WaitForBackupWithRetries(Connection connection, Settings settings, string backupId, int pollInterval, OutputInterceptor output, Action<string> onPoll)
    {
        var retry = true;

        while (retry)
        {
            try
            {
                await connection.WaitForBackup(backupId, TimeSpan.FromSeconds(pollInterval), onPoll);
                return;
            }
            catch (Exception)
            {
                retry = false;
                try
                {
                    // See if we can get a new connection (re-connect)
                    onPoll("Re-authenticating...");
                    connection = await settings.ReloadPersistedSettings().GetConnection(output);
                    retry = true;
                }
                catch (Exception)
                {
                }

                // Throw original exception if we can't re-connect
                if (!retry)
                    throw;
            }

        } while (retry) ;
    }
}
