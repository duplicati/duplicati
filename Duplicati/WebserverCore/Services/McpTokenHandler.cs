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

using System.Security.Claims;
using System.Text.Encodings.Web;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Authentication handler for MCP tokens. The scheme is only listed by the MCP endpoint,
/// so an MCP token gives no access to the rest of the API.
/// </summary>
/// <param name="options">The options for the handler</param>
/// <param name="logger">The logger</param>
/// <param name="encoder">The URL encoder</param>
/// <param name="tokenProvider">The provider reading the tokens</param>
/// <param name="tokenStore">The store of issued tokens</param>
public class McpTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IJWTTokenProvider tokenProvider,
    IMcpTokenStore tokenStore
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// The name of the scheme
    /// </summary>
    public const string SchemeName = "Mcp";

    /// <summary>
    /// The claim holding the ID of the MCP token the request was made with
    /// </summary>
    public const string TokenIdClaim = "duplicati-mcp-token-id";

    /// <summary>
    /// The prefix of the header value carrying the token
    /// </summary>
    private const string BearerPrefix = "Bearer ";

    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        IJWTTokenProvider.McpToken token;
        try
        {
            token = tokenProvider.ReadMcpToken(authHeader.Substring(BearerPrefix.Length).Trim());
        }
        catch
        {
            // Not an MCP token, or not a valid one
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // The store decides if the token is still good, so a deleted token stops working at once
        var entry = tokenStore.Get(token.TokenId);
        if (entry == null || entry.Expires <= DateTime.UtcNow)
            return Task.FromResult(AuthenticateResult.Fail("The MCP token has been revoked or has expired"));

        var identity = new ClaimsIdentity([new Claim(TokenIdClaim, entry.Id)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
