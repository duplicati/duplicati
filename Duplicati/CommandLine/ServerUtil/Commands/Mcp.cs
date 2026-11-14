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

public static class Mcp
{
    /// <summary>
    /// The environment variable holding the MCP access token
    /// </summary>
    public const string TokenEnvironmentVariable = "DUPLICATI_MCP_TOKEN";

    public static Command Create()
    {
        var tokenFileOption = new Option<FileInfo?>("--token-file")
        {
            Description = $"A file holding the MCP access token to use. The token can also be given in the environment variable {TokenEnvironmentVariable}. Without a token, the tool logs in to the server the way the other commands do, which gives the agent full access"
        };

        var cmd = new Command("mcp", "Runs an MCP server on standard input and output that forwards to the MCP endpoint of the Duplicati server, for AI agents that start MCP servers as local commands")
        {
            tokenFileOption
        };
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            // Standard output carries the protocol, so everything else that is written goes to standard error
            var protocolOutput = Console.OpenStandardOutput();
            Console.SetOut(Console.Error);

            var settings = SettingsBinder.GetSettings(parseResult);
            var output = OutputInterceptorBinder.GetConsoleInterceptor(parseResult);

            var tokenFile = parseResult.GetValue(tokenFileOption);
            var token = tokenFile != null
                ? (await File.ReadAllTextAsync(tokenFile.FullName, cancellationToken)).Trim()
                : Environment.GetEnvironmentVariable(TokenEnvironmentVariable)?.Trim();

            HttpClient client;
            Func<CancellationToken, Task<bool>> reauthenticateAsync;
            if (!string.IsNullOrWhiteSpace(token))
            {
                // An MCP token is not renewed; when it is refused the user has to issue a new one
                client = Connection.CreateTokenClient(settings, token, output);
                reauthenticateAsync = _ => Task.FromResult(false);
            }
            else
            {
                client = await LoginAsync(settings, output);
                reauthenticateAsync = async _ =>
                {
                    // The access token of a login is short-lived, so it is replaced when it runs out
                    var previous = client;
                    client = await LoginAsync(settings.ReloadPersistedSettings(), output);
                    previous.Dispose();
                    return true;
                };
            }

            var bridge = new McpBridge(
                new Uri(settings.HostUrl, "mcp"),
                () => client,
                reauthenticateAsync,
                Console.OpenStandardInput(),
                protocolOutput,
                Console.Error);

            await bridge.RunAsync(cancellationToken);
            output.SetResult(true);
        });
        return cmd;
    }

    /// <summary>
    /// Logs in to the server and creates a client with the access token of the login
    /// </summary>
    /// <param name="settings">The settings to use for the connection</param>
    /// <param name="output">Console messages interceptor</param>
    /// <returns>The client</returns>
    private static async Task<HttpClient> LoginAsync(Settings settings, OutputInterceptor output)
    {
        var accessToken = (await settings.GetConnectionAsync(output)).AccessToken
            ?? throw new UserReportedException("Failed to get an access token from the server");
        return Connection.CreateTokenClient(settings, accessToken, output);
    }
}
