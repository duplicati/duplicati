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

public static class ListBackups
{
    public static Command Create() =>
        new Command("list-backups", "List all backups")
        .WithHandler(CommandHandler.Create<Settings, OutputInterceptor>(async (settings, output) =>
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
                    output.AppendConsoleMessage(string.Empty);
                }
                output.AppendCustomObject("Backups", backupEntries.Select(id => new { Id = id.ID, Name = id.Name }).ToArray());
            }
            else
                output.AppendConsoleMessage("No backups found");

            output.SetResult(true);
        }));
}
