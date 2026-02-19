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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Duplicati.Library.Logging;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Middlewares;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Duplicati.WebserverCore.Endpoints.V1;

/// <summary>
/// Provides Entra ID (Azure AD) SSO authentication endpoints.
/// Configured via environment variables:
///   ENTRA_SSO_ENABLED=1         – enable SSO (requires the other vars to be set)
///   ENTRA_TENANT_ID             – Azure AD tenant ID (GUID or domain)
///   ENTRA_CLIENT_ID             – App registration client ID
///   ENTRA_CLIENT_SECRET         – App registration client secret
///   ENTRA_SSO_ONLY=1            – when set, the login page auto-redirects to Azure AD
///                                  and the password form is hidden
/// </summary>
public class EntraIdAuth : IEndpointV1
{
    private static readonly string LOGTAG = Log.LogTagFromType<EntraIdAuth>();

    // ── state-cache key prefixes ──────────────────────────────────────────────
    private const string StateCachePrefix = "EntraId:state:";

    // ── timing constants ──────────────────────────────────────────────────────
    /// <summary>How long an OAuth2 state/PKCE pair remains valid (covers typical IdP round-trips).</summary>
    private const int StateExpirationMinutes = 10;
    /// <summary>Allowed clock skew when validating Entra ID JWT tokens.</summary>
    private static readonly TimeSpan TokenValidationClockSkew = TimeSpan.FromMinutes(5);

    // ── environment-variable configuration (read once at startup) ────────────
    private static readonly string? TenantId =
        Environment.GetEnvironmentVariable("ENTRA_TENANT_ID")?.Trim();

    private static readonly string? ClientId =
        Environment.GetEnvironmentVariable("ENTRA_CLIENT_ID")?.Trim();

    private static readonly string? ClientSecret =
        Environment.GetEnvironmentVariable("ENTRA_CLIENT_SECRET")?.Trim();

    /// <summary>Whether SSO is fully configured and explicitly enabled.</summary>
    public static readonly bool SsoEnabled =
        Environment.GetEnvironmentVariable("ENTRA_SSO_ENABLED")?.Trim() == "1"
        && !string.IsNullOrEmpty(TenantId)
        && !string.IsNullOrEmpty(ClientId)
        && !string.IsNullOrEmpty(ClientSecret);

    /// <summary>When true the login page auto-redirects to Azure AD.</summary>
    public static readonly bool SsoOnly =
        SsoEnabled
        && Environment.GetEnvironmentVariable("ENTRA_SSO_ONLY")?.Trim() == "1";

    // ── OIDC configuration manager (singleton, caches signing keys) ──────────
    private static readonly Lazy<ConfigurationManager<OpenIdConnectConfiguration>> OidcConfigManager =
        new(() => new ConfigurationManager<OpenIdConnectConfiguration>(
            $"https://login.microsoftonline.com/{TenantId}/v2.0/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever()));

    // ──────────────────────────────────────────────────────────────────────────

    public static void Map(RouteGroupBuilder group)
    {
        // ── Config probe – always registered so the frontend can discover SSO ─
        group.MapGet("auth/entra/config", () => new
        {
            Enabled = SsoEnabled,
            AutoRedirect = SsoOnly
        });

        if (!SsoEnabled)
            return;

        // ── Start SSO flow ────────────────────────────────────────────────────
        group.MapGet("auth/entra/authorize", (
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache cache) =>
        {
            var ctx = httpContextAccessor.HttpContext!;

            // PKCE: generate code_verifier + code_challenge
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = ComputeCodeChallenge(codeVerifier);

            // CSRF protection: random opaque state token
            var state = ToBase64Url(RandomNumberGenerator.GetBytes(32));

            // Store verifier + state in cache (short-lived, single-use)
            cache.Set(
                StateCachePrefix + state,
                codeVerifier,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(StateExpirationMinutes) });

            var redirectUri = BuildRedirectUri(ctx);
            var authUrl =
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(TenantId!)}/oauth2/v2.0/authorize" +
                $"?client_id={Uri.EscapeDataString(ClientId!)}" +
                $"&response_type=code" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                $"&scope={Uri.EscapeDataString("openid profile email")}" +
                $"&state={Uri.EscapeDataString(state)}" +
                $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                $"&code_challenge_method=S256";

            return Results.Redirect(authUrl);
        });

        // ── OAuth2 callback ───────────────────────────────────────────────────
        group.MapGet("auth/entra/callback", async (
            HttpContext ctx,
            IMemoryCache cache,
            IJWTTokenProvider tokenProvider,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            // Azure AD sends either (code, state) on success or (error, …) on failure.
            var query = ctx.Request.Query;

            if (query.TryGetValue("error", out var errorVal))
            {
                var desc = query.TryGetValue("error_description", out var d) ? (string?)d : null;
                Log.WriteWarningMessage(LOGTAG, "EntraIdError", null,
                    $"Entra ID SSO error: {errorVal} – {desc}");
                return Results.Redirect("/login.html?error=sso_denied");
            }

            if (!query.TryGetValue("code", out var codeVal) ||
                !query.TryGetValue("state", out var stateVal))
                return Results.Redirect("/login.html?error=invalid_callback");

            var code = (string?)codeVal;
            var state = (string?)stateVal;

            // Validate CSRF state and retrieve PKCE verifier
            var cacheKey = StateCachePrefix + state;
            if (!cache.TryGetValue(cacheKey, out string? codeVerifier) ||
                string.IsNullOrEmpty(codeVerifier))
                return Results.Redirect("/login.html?error=invalid_state");

            cache.Remove(cacheKey); // single-use

            // Exchange authorization code for tokens
            var redirectUri = BuildRedirectUri(ctx);
            var httpClient = httpClientFactory.CreateClient();

            HttpResponseMessage tokenResp;
            try
            {
                tokenResp = await httpClient.PostAsync(
                    $"https://login.microsoftonline.com/{Uri.EscapeDataString(TenantId!)}/oauth2/v2.0/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["client_id"] = ClientId!,
                        ["client_secret"] = ClientSecret!,
                        ["code"] = code!,
                        ["redirect_uri"] = redirectUri,
                        ["code_verifier"] = codeVerifier
                    }),
                    ct);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "TokenExchangeError", ex, "Failed to exchange authorization code");
                return Results.Redirect("/login.html?error=token_exchange_failed");
            }

            if (!tokenResp.IsSuccessStatusCode)
            {
                var body = await tokenResp.Content.ReadAsStringAsync(ct);
                Log.WriteWarningMessage(LOGTAG, "TokenExchangeHttpError", null,
                    $"Token endpoint returned {(int)tokenResp.StatusCode}: {body}");
                return Results.Redirect("/login.html?error=token_exchange_failed");
            }

            string? idToken;
            try
            {
                using var doc = await JsonDocument.ParseAsync(
                    await tokenResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                idToken = doc.RootElement.GetProperty("id_token").GetString();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "TokenParseError", ex, "Failed to parse token response");
                return Results.Redirect("/login.html?error=token_parse_failed");
            }

            if (string.IsNullOrEmpty(idToken))
                return Results.Redirect("/login.html?error=no_id_token");

            // Validate the Entra ID JWT using OIDC discovery keys
            string userId;
            try
            {
                var oidcConfig = await OidcConfigManager.Value.GetConfigurationAsync(ct);

                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers =
                    [
                        $"https://login.microsoftonline.com/{TenantId}/v2.0",
                        $"https://sts.windows.net/{TenantId}/"
                    ],
                    ValidateAudience = true,
                    ValidAudience = ClientId,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    IssuerSigningKeys = oidcConfig.SigningKeys,
                    ClockSkew = TokenValidationClockSkew
                };

                var handler = new JsonWebTokenHandler();
                var result = await handler.ValidateTokenAsync(idToken, validationParams);

                if (!result.IsValid)
                {
                    Log.WriteWarningMessage(LOGTAG, "TokenValidationFailed", result.Exception,
                        "Entra ID token validation failed");
                    return Results.Redirect("/login.html?error=token_validation_failed");
                }

                // Use the stable "oid" claim (object ID) as the Duplicati user identifier.
                // "oid" is always present in Entra ID tokens; fall back only to preferred_username.
                result.Claims.TryGetValue("oid", out var oid);
                result.Claims.TryGetValue("preferred_username", out var upn);
                var subjectId = (oid as string) ?? (upn as string);
                if (string.IsNullOrEmpty(subjectId))
                {
                    Log.WriteWarningMessage(LOGTAG, "MissingSubjectClaim", null,
                        "Entra ID token missing both 'oid' and 'preferred_username' claims");
                    return Results.Redirect("/login.html?error=token_missing_subject");
                }

                userId = $"entra:{subjectId}";
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "TokenValidationError", ex, "Error validating Entra ID token");
                return Results.Redirect("/login.html?error=token_validation_failed");
            }

            // Issue a short-lived Duplicati signin token and redirect to the
            // existing signin page which converts it to a session.
            var signinToken = tokenProvider.CreateSigninToken(userId);
            return Results.Redirect($"/signin.html?token={Uri.EscapeDataString(signinToken)}");
        });
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string BuildRedirectUri(HttpContext ctx)
        => $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/v1/auth/entra/callback";

    /// <summary>Converts a byte array to a base64url-encoded string (RFC 4648 §5, no padding).</summary>
    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

    private static string GenerateCodeVerifier()
        => ToBase64Url(RandomNumberGenerator.GetBytes(32));

    private static string ComputeCodeChallenge(string codeVerifier)
        => ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
}
