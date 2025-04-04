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

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Configuration for the PreAuthTokenHandler
/// </summary>
/// <param name="AllowedTokens">The tokens that are allowed</param>
public record PreAuthTokenConfig(IReadOnlySet<string> AllowedTokens);

/// <summary>
/// Handler for pre-authenticated tokens
/// </summary>
/// <param name="options">The options for the handler</param>
/// <param name="logger">The logger</param>
/// <param name="encoder">The URL encoder</param>
/// <param name="config">The configuration</param>
public class PreAuthTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    PreAuthTokenConfig config
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// The name of the scheme
    /// </summary>
    public const string SchemeName = "PreAuth";
    /// <summary>
    /// The sheme prefix to look for in the header
    /// </summary>
    public const string SchemePrefix = SchemeName + " ";

    /// <summary>
    /// Handles the authentication
    /// </summary>
    /// <returns>The result of the authentication</returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;
        var authHeader = Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith(SchemePrefix, StringComparison.OrdinalIgnoreCase))
            token = authHeader.Substring(SchemePrefix.Length).Trim();

        // Support for websocket authentication (should be fixed with messages)
        if (string.IsNullOrWhiteSpace(authHeader) && string.IsNullOrWhiteSpace(token))
            token = Request.Query["token"];

        if (!string.IsNullOrWhiteSpace(token) && config.AllowedTokens.Contains(token))
        {
            var identity = new ClaimsIdentity([], Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid PreAuth token"));
    }
}