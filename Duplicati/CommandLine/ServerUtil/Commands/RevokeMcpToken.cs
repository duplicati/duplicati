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

public static class RevokeMcpToken
{
    public static Command Create()
    {
        var tokenArgument = new Argument<string>("token")
        {
            Description = "The token to revoke, either ID or exact name (case-insensitive)",
            Arity = ArgumentArity.ExactlyOne
        };

        var cmd = new Command("revoke-mcp-token", "Revokes a token issued for the MCP endpoint, which stops it from working at once")
        {
            tokenArgument
        };
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var settings = SettingsBinder.GetSettings(parseResult);
            var output = OutputInterceptorBinder.GetConsoleInterceptor(parseResult);
            var token = parseResult.GetValue(tokenArgument)!;

            var connection = await settings.GetConnectionAsync(output);
            var matches = (await connection.ListMcpTokensAsync())
                .Where(x => string.Equals(x.Id, token) || string.Equals(x.Name, token, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                throw new UserReportedException("No MCP token found with supplied ID or name");
            if (matches.Count > 1)
                throw new UserReportedException("More than one MCP token has that name, use the ID from list-mcp-tokens");

            await connection.DeleteMcpTokenAsync(matches[0].Id);
            output.AppendConsoleMessage($"Revoked MCP token {matches[0].Name} (ID: {matches[0].Id})");
            output.SetResult(true);
        });
        return cmd;
    }
}
