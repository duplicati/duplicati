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
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto.V2;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class IssueMcpToken
{
    public static Command Create()
    {
        var nameOption = new Option<string>("--name")
        {
            Description = "A name for the token, usually the agent it is for",
            Required = true
        };
        var levelOption = new Option<McpTokenLevel>("--level")
        {
            Description = "What the token allows: Read (list and browse), Backup (also run backups) or Restore (also restore files)",
            DefaultValueFactory = _ => McpTokenLevel.Restore
        };
        var backupsOption = new Option<string[]>("--backup")
        {
            Description = "A backup the token can be used with, either ID or exact name (case-insensitive). Repeat for more backups; leave out to allow all backups",
            AllowMultipleArgumentsPerToken = true
        };
        var restoreRootsOption = new Option<string[]>("--restore-root")
        {
            Description = "A folder the token can restore into, besides the original location of the files. Repeat for more folders",
            AllowMultipleArgumentsPerToken = true
        };
        var expiresOption = new Option<string>("--expires-in")
        {
            Description = "How long the token is valid, such as 30D or 1Y",
            DefaultValueFactory = _ => "90D"
        };

        var cmd = new Command("issue-mcp-token", "Issues a token that gives an AI agent access to the MCP endpoint")
        {
            nameOption,
            levelOption,
            backupsOption,
            restoreRootsOption,
            expiresOption
        };
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var settings = SettingsBinder.GetSettings(parseResult);
            var output = OutputInterceptorBinder.GetConsoleInterceptor(parseResult);
            var backups = parseResult.GetValue(backupsOption) ?? [];
            var restoreRoots = parseResult.GetValue(restoreRootsOption) ?? [];

            var connection = await settings.GetConnectionAsync(output);

            string[]? backupIds = null;
            if (backups.Length > 0)
            {
                var known = (await connection.ListBackupsAsync()).ToList();
                backupIds = backups
                    .Select(b => known.FirstOrDefault(x => string.Equals(x.ID, b) || string.Equals(x.Name, b, StringComparison.OrdinalIgnoreCase))?.ID
                        ?? throw new UserReportedException($"No backup found with the ID or name \"{b}\""))
                    .Distinct()
                    .ToArray();
            }

            var result = await connection.CreateMcpTokenAsync(new CreateMcpTokenInputDto(
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(levelOption),
                backupIds,
                restoreRoots,
                parseResult.GetValue(expiresOption)));

            var url = new Uri(settings.HostUrl, "mcp");
            output.AppendConsoleMessage($"Token \"{result.Token.Name}\" issued with ID {result.Token.Id}, level {result.Token.Level}, valid until {result.Token.Expires.ToLocalTime():yyyy-MM-dd HH:mm}.");
            output.AppendConsoleMessage("The token is only accepted by the MCP endpoint, and only while the endpoint is enabled on the server (--webservice-enable-mcp-server).");
            output.AppendConsoleMessage("It is shown this once and cannot be retrieved again. Revoke it with the revoke-mcp-token command.");
            output.AppendConsoleMessage(string.Empty);
            output.AppendConsoleMessage($"MCP endpoint: {url}");
            output.AppendConsoleMessage($"Authorization: Bearer {result.AccessToken}");
            output.AppendConsoleMessage(string.Empty);
            output.AppendCustomObject("Token", new { result.Token.Id, result.Token.Name, result.Token.Expires, Level = result.Token.Level.ToString(), Url = url.ToString(), Token = result.AccessToken });
            output.SetResult(true);
        });
        return cmd;
    }
}
