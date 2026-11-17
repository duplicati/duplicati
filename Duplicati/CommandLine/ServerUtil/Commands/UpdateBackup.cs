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

public static class UpdateBackup
{
    public static Command Create()
    {
        var backupArgument = new Argument<string>("backup")
        {
            Description = "The backup to update, either ID or exact name (case-insensitive)",
            Arity = ArgumentArity.ExactlyOne
        };
        var fileArgument = new Argument<FileInfo>("file")
        {
            Description = "A JSON file with the parts of the configuration to change, for example {\"Backup\": {\"Settings\": {\"--volume-size\": \"80mb\", \"--list-verify-uploads\": null}}}. "
                + "Properties given under \"Backup\" or \"Schedule\" replace the current ones; entries under \"Backup\".\"Settings\" set the named setting, and null removes it. "
                + "\"Schedule\": null removes the schedule. Anything not mentioned is kept.",
            Arity = ArgumentArity.ExactlyOne
        };

        var cmd = new Command("update-backup", "Updates parts of a backup configuration")
        {
            backupArgument,
            fileArgument
        };
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var settings = SettingsBinder.GetSettings(parseResult);
            var output = OutputInterceptorBinder.GetConsoleInterceptor(parseResult);
            var backup = parseResult.GetValue(backupArgument)!;
            var file = parseResult.GetValue(fileArgument)!;

            if (!file.Exists)
                throw new UserReportedException($"File {file.FullName} does not exist");

            var patch = BackupPatch.Parse(await File.ReadAllTextAsync(file.FullName, cancellationToken));

            var connection = await settings.GetConnectionAsync(output);

            var matchingBackup = (await connection.ListBackupsAsync())
                .FirstOrDefault(b => string.Equals(b.Name, backup, StringComparison.OrdinalIgnoreCase) || string.Equals(b.ID, backup));

            if (matchingBackup == null)
                throw new UserReportedException("No backup found with supplied ID or name");

            output.AppendConsoleMessage($"Updating backup {matchingBackup.Name} (ID: {matchingBackup.ID}) from {file.FullName}");
            var result = await connection.UpdateBackupAsync(matchingBackup.ID, patch);

            output.AppendConsoleMessage($"Updated \"{result.Name}\" with ID {result.ID}");
            output.AppendCustomObject("Updated", new { Id = result.ID, Name = result.Name });
            output.SetResult(true);
        });
        return cmd;
    }
}
