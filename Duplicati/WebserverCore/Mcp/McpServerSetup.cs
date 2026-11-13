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

using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Mcp.Tools;
using Duplicati.WebserverCore.Middlewares;
using Duplicati.WebserverCore.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Duplicati.WebserverCore.Mcp;

/// <summary>
/// Registration of the optional Model Context Protocol (MCP) endpoint
/// </summary>
public static class McpServerSetup
{
    /// <summary>
    /// The path the MCP endpoint is served on
    /// </summary>
    public const string EndpointPath = "/mcp";

    /// <summary>
    /// The instructions the server gives to a connecting agent
    /// </summary>
    private const string Instructions =
        "Duplicati is a backup server. Use it as a safety net around risky work: "
        + "1) list_backups to find a backup whose source paths cover the files you are about to change; "
        + "2) run_backup, then wait_for_task until finished is true, and check that the result is Success or Warning; keep the restorePoint time; "
        + "3) do your work; "
        + "4) if needed, restore with that time, and wait_for_task again. "
        + "Tasks run one at a time and can take long: wait_for_task returns after a bounded time with finished false, so call it again. "
        + "A restore writes backed up files back but does not delete files created later, so it is not a full rollback. "
        + "Backups cannot be created or reconfigured through these tools; ask the user if no backup covers the files.";

    /// <summary>
    /// Adds the MCP server and its tools
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection</returns>
    public static IServiceCollection AddDuplicatiMcp(this IServiceCollection services)
    {
        services.AddTransient<McpTaskService>();
        services.AddTransient<McpScope>();
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "duplicati",
                    Title = "Duplicati",
                    Version = Library.AutoUpdater.UpdaterManager.SelfVersion.Version ?? "0.0.0"
                };
                options.ServerInstructions = Instructions;
            })
            // Stateless: no sessions to track, and progress travels on the response of the waiting call
            .WithHttpTransport(options => options.Stateless = true)
            .WithRequestFilters(filters => filters.AddCallToolFilter(ReportToolErrors))
            .WithTools<ServerTools>()
            .WithTools<BackupTools>()
            .WithTools<TaskTools>()
            .WithTools<BrowseTools>()
            .WithTools<RestoreTools>();

        return services;
    }

    /// <summary>
    /// Answers a tool call that is refused with the reason alone, and names the arguments a call is missing.
    /// Without this the MCP server logs every refusal as an unhandled exception with a stack trace,
    /// and reports a missing argument to the agent without saying which one.
    /// </summary>
    /// <param name="next">The handler that runs the tool</param>
    /// <returns>The handler that reports the errors</returns>
    private static McpRequestHandler<CallToolRequestParams, CallToolResult> ReportToolErrors(McpRequestHandler<CallToolRequestParams, CallToolResult> next)
        => async (request, cancellationToken) =>
        {
            var missing = GetMissingArguments(request.MatchedPrimitive as McpServerTool, request.Params?.Arguments);
            if (missing.Count > 0)
                return CreateToolError(missing.Count == 1
                    ? $"The tool {request.Params?.Name} needs the argument \"{missing[0]}\"."
                    : $"The tool {request.Params?.Name} needs the arguments {string.Join(", ", missing.Select(x => $"\"{x}\""))}.");

            try
            {
                return await next(request, cancellationToken).ConfigureAwait(false);
            }
            // A protocol error is answered as such by the MCP server, the others are answers from a tool
            catch (McpException ex) when (ex is not McpProtocolException)
            {
                return CreateToolError(ex.Message);
            }
        };

    /// <summary>
    /// Finds the required arguments that a tool call does not have
    /// </summary>
    /// <param name="tool">The tool that is called, or <c>null</c> if there is no such tool</param>
    /// <param name="arguments">The arguments of the call, if any</param>
    /// <returns>The names of the missing arguments</returns>
    public static List<string> GetMissingArguments(McpServerTool? tool, IDictionary<string, JsonElement>? arguments)
    {
        if (tool == null
            || tool.ProtocolTool.InputSchema.ValueKind != JsonValueKind.Object
            || !tool.ProtocolTool.InputSchema.TryGetProperty("required", out var required)
            || required.ValueKind != JsonValueKind.Array)
            return [];

        return required.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => x != null && (arguments == null || !arguments.ContainsKey(x)))
            .Select(x => x!)
            .ToList();
    }

    /// <summary>
    /// Creates the answer to a tool call that failed for a reason the agent can act on
    /// </summary>
    /// <param name="message">The reason</param>
    /// <returns>The answer</returns>
    private static CallToolResult CreateToolError(string message)
        => new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }]
        };

    /// <summary>
    /// Adds the checks that guard the MCP endpoint. They run before authentication,
    /// so a server with the feature turned off answers like a server without it.
    /// </summary>
    /// <param name="app">The application</param>
    /// <returns>The application</returns>
    public static WebApplication UseDuplicatiMcpAccessControl(this WebApplication app)
    {
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(EndpointPath),
            branch => branch.Use(async (context, next) =>
            {
                var connection = context.RequestServices.GetRequiredService<Connection>();
                if (!connection.ApplicationSettings.EnableMcpServer)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var rejection = GetRejection(context.Request, context.RequestServices.GetRequiredService<IHostnameValidator>(), PreSharedKeyFilter.PreSharedKey);
                if (rejection != null)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync(rejection);
                    return;
                }

                await next(context);
            }));

        return app;
    }

    /// <summary>
    /// Maps the MCP endpoint, which accepts an MCP token or the credentials of the API
    /// </summary>
    /// <param name="app">The application</param>
    public static void MapDuplicatiMcp(this WebApplication app)
        => app.MapMcp(EndpointPath).RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = $"{DuplicatiWebserver.CombinedSchemeName},{McpTokenHandler.SchemeName}"
        });

    /// <summary>
    /// Checks the parts of a request that protect a local server from requests made by a web page:
    /// the host name, the origin, and the pre-shared key when one is configured
    /// </summary>
    /// <param name="request">The request</param>
    /// <param name="hostnameValidator">The validator of host names</param>
    /// <param name="preSharedKey">The pre-shared key that requests must carry, or <c>null</c> if none is configured</param>
    /// <returns>The reason the request is rejected, or <c>null</c> if it is acceptable</returns>
    public static string? GetRejection(HttpRequest request, IHostnameValidator hostnameValidator, string? preSharedKey)
    {
        if (!hostnameValidator.IsValidHostname(request.Host.Host))
            return "Invalid hostname";

        // A browser adds the origin of the page making the request; other clients send none
        foreach (var origin in request.Headers.Origin)
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || !hostnameValidator.IsValidHostname(originUri.Host))
                return "Invalid origin";

        if (!string.IsNullOrEmpty(preSharedKey) && !PreSharedKeyFilter.HasValidPreSharedKey(request, preSharedKey))
            return "Invalid PreSharedKey";

        return null;
    }
}
