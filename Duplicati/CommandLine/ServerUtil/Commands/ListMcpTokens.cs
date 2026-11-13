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

public static class ListMcpTokens
{
    public static Command Create()
    {
        var cmd = new Command("list-mcp-tokens", "Lists the tokens issued for the MCP endpoint");
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var settings = SettingsBinder.GetSettings(parseResult);
            var output = OutputInterceptorBinder.GetConsoleInterceptor(parseResult);

            var tokens = await (await settings.GetConnectionAsync(output)).ListMcpTokensAsync();
            if (tokens.Length == 0)
                output.AppendConsoleMessage("No MCP tokens issued");

            foreach (var token in tokens)
            {
                output.AppendConsoleMessage($"{token.Id}: {token.Name}");
                output.AppendConsoleMessage($"  Level: {token.Level}");
                output.AppendConsoleMessage($"  Backups: {(token.BackupIds == null ? "all" : string.Join(", ", token.BackupIds))}");
                if (token.RestoreRoots.Length > 0)
                    output.AppendConsoleMessage($"  Restore folders: {string.Join(", ", token.RestoreRoots)}");
                output.AppendConsoleMessage($"  Created: {token.Created.ToLocalTime():yyyy-MM-dd HH:mm}");
                output.AppendConsoleMessage($"  Expires: {token.Expires.ToLocalTime():yyyy-MM-dd HH:mm}{(token.Expired ? " (expired)" : "")}");
                output.AppendConsoleMessage(string.Empty);
            }

            output.AppendCustomObject("Tokens", tokens.Select(x => new { x.Id, x.Name, Level = x.Level.ToString(), x.BackupIds, x.RestoreRoots, x.Created, x.Expires, x.Expired }).ToArray());
            output.SetResult(true);
        });
        return cmd;
    }
}
