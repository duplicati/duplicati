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
using Duplicati.WebserverCore.Dto.V2;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Services;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V2;

/// <summary>
/// Endpoints for managing the tokens that give an agent access to the MCP endpoint.
/// They require the credentials of the API, so an MCP token cannot be used to issue another.
/// </summary>
public class McpTokens : IEndpointV2
{
    /// <summary>
    /// The lifetime of a token when the request does not give one
    /// </summary>
    public const string DefaultLifetime = "90D";

    /// <summary>
    /// The longest lifetime a token can be given
    /// </summary>
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(3660);

    /// <summary>
    /// The shortest lifetime a token can be given
    /// </summary>
    private static readonly TimeSpan MinLifetime = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The longest name a token can be given
    /// </summary>
    private const int MaxNameLength = 100;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/mcp/tokens", ([FromServices] IMcpTokenStore tokenStore)
            => ExecuteList(tokenStore))
            .RequireAuthorization();

        group.MapPost("/mcp/tokens", ([FromServices] IMcpTokenStore tokenStore, [FromServices] Connection connection, [FromBody] CreateMcpTokenInputDto input)
            => ExecuteCreate(tokenStore, connection, input))
            .RequireAuthorization();

        group.MapDelete("/mcp/token/{id}", ([FromServices] IMcpTokenStore tokenStore, [FromRoute] string id)
            => ExecuteDelete(tokenStore, id))
            .RequireAuthorization();
    }

    private static ResponseEnvelope<McpTokenDto[]> ExecuteList(IMcpTokenStore tokenStore)
        => ResponseEnvelope.Result(tokenStore.List().Select(ToDto).ToArray());

    private static ResponseEnvelope<CreateMcpTokenOutputDto> ExecuteCreate(IMcpTokenStore tokenStore, Connection connection, CreateMcpTokenInputDto input)
    {
        // A token for a server that is off would start to work when the server is turned on
        if (!connection.ApplicationSettings.EnableMcpServer)
            throw new NotFoundException(McpTokenStore.DisabledMessage);

        var name = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new BadRequestException("The token needs a name");
        if (name.Length > MaxNameLength)
            throw new BadRequestException($"The token name can be at most {MaxNameLength} characters");
        if (!Enum.IsDefined(input.Level))
            throw new BadRequestException("Unknown permission level");

        TimeSpan lifetime;
        try
        {
            lifetime = Library.Utility.Timeparser.ParseTimeSpan(string.IsNullOrWhiteSpace(input.ExpiresIn) ? DefaultLifetime : input.ExpiresIn);
        }
        catch (Exception)
        {
            throw new BadRequestException($"The lifetime \"{input.ExpiresIn}\" is not a valid time span, use a value such as 30D or 1Y");
        }
        if (lifetime < MinLifetime || lifetime > MaxLifetime)
            throw new BadRequestException("The lifetime must be between one minute and ten years");

        // An empty list would be a token that can do nothing, so it is treated as a mistake
        var backupIds = input.BackupIds?.Select(x => x?.Trim() ?? string.Empty).Distinct().ToArray();
        if (backupIds != null)
        {
            if (backupIds.Length == 0)
                throw new BadRequestException("The list of backups is empty; leave it out to allow all backups");

            var known = connection.Backups.Select(x => x.ID).ToHashSet();
            var unknown = backupIds.Where(x => !known.Contains(x)).ToList();
            if (unknown.Count > 0)
                throw new BadRequestException($"Unknown backup ID: {string.Join(", ", unknown)}");
        }

        var restoreRoots = (input.RestoreRoots ?? []).Select(x => x?.Trim() ?? string.Empty).Where(x => x.Length > 0).Distinct().ToArray();
        if (restoreRoots.Length > 0 && input.Level < McpTokenLevel.Restore)
            throw new BadRequestException("Restore folders can only be given for a token with the Restore level");
        var relative = restoreRoots.Where(x => !Path.IsPathFullyQualified(x)).ToList();
        if (relative.Count > 0)
            throw new BadRequestException($"Restore folders must be full paths: {string.Join(", ", relative)}");

        var (entry, token) = tokenStore.Create(name, input.Level, backupIds, restoreRoots, lifetime);
        return ResponseEnvelope.Result(new CreateMcpTokenOutputDto(ToDto(entry), token));
    }

    private static ResponseEnvelope<object> ExecuteDelete(IMcpTokenStore tokenStore, string id)
    {
        if (!tokenStore.Delete(id))
            throw new NotFoundException("No such MCP token");

        return ResponseEnvelope.Result<object>(null);
    }

    private static McpTokenDto ToDto(McpTokenEntry entry)
        => new(entry.Id, entry.Name, entry.Created, entry.Expires, entry.Expires <= DateTime.UtcNow, entry.Level, entry.BackupIds, entry.RestoreRoots);
}
