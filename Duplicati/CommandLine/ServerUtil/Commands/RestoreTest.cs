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

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class RestoreTest
{
    public static Command Create()
    {
        var backupArgument = new Argument<string>("backup")
        {
            Description = "The backup to run a restore test for, either ID or exact name (case-insensitive)",
            Arity = ArgumentArity.ExactlyOne
        };
        var quietOption = new Option<bool>("--quiet")
        {
            Description = "Do not print progress messages"
        };

        var cmd = new Command("restore-test", "Queues a restore test for a backup")
        {
            backupArgument,
            quietOption
        };
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var settings = SettingsBinder.GetSettings(parseResult);
            var output = OutputInterceptorBinder.GetConsoleInterceptor(parseResult);
            var backup = parseResult.GetValue(backupArgument)!;
            var quiet = parseResult.GetValue(quietOption);

            var connection = await settings.GetConnectionAsync(output);

            var matchingBackup = (await connection.ListBackupsAsync())
                .FirstOrDefault(b => string.Equals(b.Name, backup, StringComparison.OrdinalIgnoreCase) || string.Equals(b.ID, backup));

            if (matchingBackup == null)
                throw new UserReportedException("No backup found with supplied ID or name");

            if (!quiet)
                output.AppendConsoleMessage($"Queueing restore test for {matchingBackup.Name} (ID: {matchingBackup.ID})");

            await connection.RunRestoreTestAsync(matchingBackup.ID);

            // The test is only queued; its outcome arrives through the backup's reports
            output.SetResult(true);
        });
        return cmd;
    }
}
