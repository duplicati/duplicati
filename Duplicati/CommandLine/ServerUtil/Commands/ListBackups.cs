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
using System.Globalization;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class ListBackups
{
    public static Command Create() =>
        new Command("list-backups", "List all backups")
        {
            new Option<bool>("--detailed", "Show detailed information about each backup")
            {
                IsRequired = false
            },
        }
        .WithHandler(CommandHandler.Create<Settings, OutputInterceptor, bool>(async (settings, output, detailed) =>
        {
            var bks = await (await settings.GetConnection(output)).ListBackups();

            var backupEntries = bks as Connection.BackupEntry[] ?? bks.ToArray();
            if (backupEntries.Any())
            {
                foreach (var bk in backupEntries)
                {
                    output.AppendConsoleMessage($"{bk.ID}: {bk.Name}");
                    if (!string.IsNullOrEmpty(bk.Description))
                        output.AppendConsoleMessage($"  {bk.Description}");

                    if (detailed)
                    {
                        void WriteDetail(string label, string key, Func<string, string>? formatter = null)
                        {
                            var value = bk.Metadata?.GetValueOrDefault(key);
                            if (string.IsNullOrWhiteSpace(value))
                                return;
                            if (formatter != null)
                                value = formatter(value);

                            if (string.IsNullOrWhiteSpace(value))
                                return;

                            output.AppendConsoleMessage($"  {label}: {value}");
                        }

                        WriteDetail("Last backup", "LastBackupDate", s => Library.Utility.Utility.TryDeserializeDateTime(s, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : s);
                        if (!string.IsNullOrWhiteSpace(bk.Schedule?.Time))
                        {
                            var timestring = DateTime.TryParseExact(bk.Schedule.Time, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var scheduleDt)
                                ? scheduleDt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                                : bk.Schedule.Time;
                            output.AppendConsoleMessage($"  Schedule: {bk.Schedule.Repeat} at {timestring}");
                        }

                        WriteDetail("Last duration", "LastBackupDuration", s => TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts) ? ts.ToString() : s);
                        WriteDetail("Source files size", "SourceFilesSize", s => Library.Utility.Utility.FormatSizeString(long.Parse(s)));
                        WriteDetail("Target files size", "TargetFilesSize", s => Library.Utility.Utility.FormatSizeString(long.Parse(s)));
                        WriteDetail("Versions", "BackupListCount");

                        Library.Utility.Utility.TryDeserializeDateTime(bk.Metadata?.GetValueOrDefault("LastErrorDate") ?? "", out var lastErrorDt);
                        Library.Utility.Utility.TryDeserializeDateTime(bk.Metadata?.GetValueOrDefault("LastBackupDate") ?? "", out var lastBackupDt);
                        var lastErrorMessage = bk.Metadata?.GetValueOrDefault("LastErrorMessage") ?? "";
                        if (lastErrorDt > lastBackupDt && !string.IsNullOrWhiteSpace(lastErrorMessage))
                            output.AppendConsoleMessage($"  Last error: {lastErrorDt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} - {lastErrorMessage}");
                    }
                    output.AppendConsoleMessage(string.Empty);
                }

                output.AppendCustomObject("Backups", backupEntries.Select(id => new
                {
                    Id = id.ID,
                    Name = id.Name,
                    Metadata = id.Metadata,
                    Schedule = id.Schedule
                }).ToArray());
            }
            else
                output.AppendConsoleMessage("No backups found");

            output.SetResult(true);
        }));
}
