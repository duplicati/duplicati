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

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Duplicati.WebserverCore.Middlewares;

public sealed class QnapAuthOptions
{
    /// <summary>
    /// URL of the QTS authLogin.cgi endpoint, queried with "?sid=...".
    /// The default points to the QTS web server on loopback, which is also
    /// reachable from inside the QPKG chroot environment.
    /// </summary>
    public string AuthLoginUrl { get; set; } = GetEnvArg("QNAP_AUTH_LOGIN_URL", "http://127.0.0.1:8080/cgi-bin/authLogin.cgi");

    /// <summary>
    /// Name of the cookie carrying the QTS session id
    /// </summary>
    public string SidCookieName { get; set; } = GetEnvArg("QNAP_SID_COOKIE", "NAS_SID");

    /// <summary>
    /// If set, use this username instead of querying the auth endpoint (mostly for testing).
    /// </summary>
    public string? ForcedUsername { get; set; } = GetEnvArg("QNAP_USERNAME");

    /// <summary>
    /// Admin flag used together with <see cref="ForcedUsername"/> (mostly for testing).
    /// </summary>
    public bool ForcedIsAdmin { get; set; } = GetEnvArg("QNAP_IS_ADMIN", "1") == "1";

    /// <summary>
    /// Allow all QTS users (not only admins) if QNAP_ALL_USERS=1; otherwise admin-only.
    /// </summary>
    public bool AdminOnly { get; set; } = !(GetEnvArg("QNAP_ALL_USERS", "0") == "1");

    /// <summary>
    /// If true, enable the middleware. Controlled by QNAP_AUTH_ENABLED=1/0.
    /// </summary>
    public bool Enabled { get; set; } = GetEnvArg("QNAP_AUTH_ENABLED", "0") == "1";

    /// <summary>
    /// If true, emits detailed debug logging of the authentication flow.
    /// Controlled by QNAP_AUTH_DEBUG=1/0.
    /// </summary>
    public bool DebugLogging { get; set; } = GetEnvArg("QNAP_AUTH_DEBUG", "0") == "1";

    /// <summary>
    /// Cache validity per (remote ip/port, sid) to avoid hitting QTS on every request.
    /// </summary>
    public TimeSpan LoginCacheTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cache validity per session id cookie to avoid hitting QTS on every request.
    /// </summary>
    public TimeSpan AuthCacheTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Timeout for the request to the QTS auth endpoint.
    /// </summary>
    public TimeSpan AuthRequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Prefixes to protect. Defaults to /api, /notifications.
    /// Set QNAP_PROTECT_PREFIXES to override (comma-separated).
    /// </summary>
    public string[] ProtectedPathPrefixes { get; set; } =
        (GetEnvArg("QNAP_PROTECT_PREFIXES") ?? "/api,/notifications")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();

    /// <summary>
    /// If true, trust X-Real-IP / X-Real-Port headers (set by reverse proxy).
    /// Controlled by QNAP_TRUST_XREAL=1/0.
    /// </summary>
    public bool TrustXRealHeaders { get; set; } = GetEnvArg("QNAP_TRUST_XREAL", "1") == "1";

    /// <summary>
    /// Get environment variable or default value.
    /// </summary>
    /// <param name="key">The environment variable key</param>
    /// <param name="default">The default value if the environment variable is not set or empty.</param>
    /// <returns>The environment variable value or the default value</returns>
    [return: NotNullIfNotNull(nameof(@default))]
    internal static string? GetEnvArg(string key, string? @default = null)
    {
        var res = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(res) ? @default : res.Trim();
    }

    /// <summary>
    /// Checks the options for misconfiguration, returning human-readable
    /// problem descriptions. An empty list means the configuration looks valid.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (!Uri.TryCreate(AuthLoginUrl, UriKind.Absolute, out var authUri)
            || (authUri.Scheme != Uri.UriSchemeHttp && authUri.Scheme != Uri.UriSchemeHttps))
            problems.Add($"QNAP_AUTH_LOGIN_URL is not an absolute http(s) URL: '{AuthLoginUrl}'");

        if (string.IsNullOrWhiteSpace(SidCookieName))
            problems.Add("QNAP_SID_COOKIE is empty; requests can never be authenticated");

        if (ProtectedPathPrefixes.Length == 0)
            problems.Add("QNAP_PROTECT_PREFIXES is empty; no paths will be protected");

        return problems;
    }
}

/// <summary>
/// Middleware for QNAP QTS integrated authentication.
/// </summary>
/// <remarks>
/// QTS on QNAP NAS devices validates login sessions via the authLogin.cgi endpoint.
/// This middleware queries the endpoint with the session id from the "NAS_SID" cookie
/// and requires the session to be valid (authPassed=1). By default it also requires
/// that the authenticated user is an administrator (isAdmin=1).
/// For endpoints that involve static content (i.e., html, js, css), the middleware
/// checks that the session id has been authenticated recently, avoiding repeated
/// requests to the endpoint.
/// To avoid slowdowns, the middleware caches authentication results for a short period of time.
/// Logging is silent by default: per-request events are only emitted when debug logging
/// is enabled (QNAP_AUTH_DEBUG=1). Warnings are reserved for misconfiguration, reported
/// once at startup, and for rate-limited auth endpoint outages.
/// </remarks>
public sealed class QnapAuthMiddleware
{
    /// <summary>
    /// Shared client for auth requests; proxy use is disabled because the
    /// endpoint is on loopback
    /// </summary>
    private static readonly HttpClient AuthHttpClient = new HttpClient(new HttpClientHandler { UseProxy = false });

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly QnapAuthOptions _opt;

    /// <summary>
    /// Tracks whether an auth-endpoint failure has been logged as a warning since
    /// the last successful endpoint response (0 = not reported, 1 = reported).
    /// Prevents repeated failures from flooding the log.
    /// </summary>
    private int _authEndpointFailureReported;

    public QnapAuthMiddleware(RequestDelegate next, IMemoryCache cache, QnapAuthOptions options)
    {
        // WARNING: This module is written for Duplicati which does not have a concept of "users" or "roles".
        // If the code is adapted for other uses, care must be taken to ensure that user identities and roles
        // are handled securely and appropriately. Specifically, the caching mechanism needs to be revised to
        // support multiple users and roles.

        _next = next ?? throw new ArgumentNullException(nameof(next));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _opt = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check if this is an API call, or a static content call
        var isApiCall = IsProtectedPath(context.Request.Path);

        // Static content calls are GET or HEAD requests to non-API paths
        var isStaticContentCall = !isApiCall && new[] { "GET", "HEAD" }.Contains(context.Request.Method.ToUpperInvariant());

        // The session id cookie is required for authLogin.cgi
        var sid = context.Request.Cookies.TryGetValue(_opt.SidCookieName, out var sidVal) ? sidVal : null;

        LogDebug("QnapAuthRequest", $"Handling {context.Request.Method} {context.Request.Path}, isApiCall: {isApiCall}, isStaticContentCall: {isStaticContentCall}, sid: {MaskSid(sid)}");

        // Static content calls get "authenticated but no per-request validation"
        // if the session id itself has been authenticated recently
        if (isStaticContentCall)
        {
            if (!string.IsNullOrWhiteSpace(sid) && _cache.TryGetValue(BuildAuthCacheKey(sid), out _))
            {
                // Authenticated recently, allow
                LogDebug("QnapAuthStaticCacheHit", $"Allowing static content {context.Request.Path} with cached sid: {MaskSid(sid)}");
                await _next(context);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(sid))
        {
            LogDebug("QnapAuthMissingSid", $"Rejecting {context.Request.Method} {context.Request.Path} because the cookie '{_opt.SidCookieName}' is missing; received cookies: {string.Join(", ", context.Request.Cookies.Keys)}");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Request not authorized");
            return;
        }

        // Build cache key for the login from this specific client
        var loginCacheKey = BuildLoginCacheKey(context, sid);

        // Check cache so we do not need to query the auth endpoint for every request
        if (!string.IsNullOrWhiteSpace(loginCacheKey) && _cache.TryGetValue(loginCacheKey, out _))
        {
            LogDebug("QnapAuthLoginCacheHit", $"Allowing {context.Request.Method} {context.Request.Path} with cached login for sid: {MaskSid(sid)}");
            await _next(context);
            return;
        }

        bool authPassed;
        bool isAdmin;
        var username = _opt.ForcedUsername;
        if (!string.IsNullOrWhiteSpace(_opt.ForcedUsername))
        {
            LogDebug("QnapAuthForced", $"Using forced username '{_opt.ForcedUsername}' (isAdmin: {_opt.ForcedIsAdmin}) for {context.Request.Method} {context.Request.Path}");
            authPassed = true;
            isAdmin = _opt.ForcedIsAdmin;
        }
        else
        {
            var url = $"{_opt.AuthLoginUrl}?sid={Uri.EscapeDataString(sid)}";
            LogDebug("QnapAuthValidating", $"Validating sid {MaskSid(sid)} for {context.Request.Method} {context.Request.Path} via '{_opt.AuthLoginUrl}'");

            string response;
            try
            {
                // Query the QTS auth endpoint to validate the session
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                cts.CancelAfter(_opt.AuthRequestTimeout);
                response = await AuthHttpClient.GetStringAsync(url, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Endpoint unreachable: likely misconfiguration or a QTS outage.
                // Log the first failure as a warning, but never log per request;
                // subsequent failures are only visible with debug logging enabled.
                if (Interlocked.Exchange(ref _authEndpointFailureReported, 1) == 0)
                    LogWarning("QnapAuthRequestFailed", $"The QTS auth request to '{_opt.AuthLoginUrl}' failed; rejecting requests until it recovers. Further failures are only logged with QNAP_AUTH_DEBUG=1", ex);
                LogDebug("QnapAuthRequestFailed", $"Rejecting {context.Request.Method} {context.Request.Path} because the QTS auth request to '{_opt.AuthLoginUrl}' failed for sid {MaskSid(sid)}", ex);
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("Authentication service unavailable");
                return;
            }

            // Endpoint responded: allow a future failure to be logged as a warning again
            Interlocked.Exchange(ref _authEndpointFailureReported, 0);

            authPassed = GetElementValue(response, "authPassed") == "1";
            isAdmin = GetElementValue(response, "isAdmin") == "1";
            username = GetElementValue(response, "username") ?? GetElementValue(response, "user");

            LogDebug("QnapAuthResult", $"Result for sid {MaskSid(sid)}: authPassed: {authPassed}, isAdmin: {isAdmin}, username: {username}, raw response: {Truncate(response)}");
        }

        if (!authPassed)
        {
            LogDebug("QnapAuthFailed", $"Rejecting {context.Request.Method} {context.Request.Path} because the QTS session {MaskSid(sid)} is not valid (authPassed != 1)");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Request not authorized");
            return;
        }

        if (_opt.AdminOnly && !isAdmin)
        {
            LogDebug("QnapAuthNotAdmin", $"Rejecting {context.Request.Method} {context.Request.Path} because QTS user '{username}' is not an administrator (isAdmin != 1)");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Administrator login required");
            return;
        }

        LogDebug("QnapAuthSuccess", $"Authenticated QTS user '{username}' with sid {MaskSid(sid)} for {context.Request.Method} {context.Request.Path}");

        // Auth OK: cache (only if cacheKey available)
        if (!string.IsNullOrWhiteSpace(loginCacheKey))
            _cache.Set(loginCacheKey, true, _opt.LoginCacheTimeout);

        // Auth OK: cache session id too
        _cache.Set(BuildAuthCacheKey(sid), true, _opt.AuthCacheTimeout);

        await _next(context);
    }

    private bool IsProtectedPath(PathString path)
    {
        var p = path.Value ?? string.Empty;
        return _opt.ProtectedPathPrefixes.Any(prefix =>
            p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts the value of an XML element, supporting both CDATA and plain content,
    /// for example: &lt;authPassed&gt;&lt;![CDATA[1]]&gt;&lt;/authPassed&gt;
    /// </summary>
    private static string? GetElementValue(string xml, string name)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var m = Regex.Match(
            xml,
            "<" + name + @"(?:\s[^>]*)?>\s*(?:<!\[CDATA\[(?<v>.*?)\]\]>|(?<v>[^<]*))\s*</" + name + ">",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return m.Success ? m.Groups["v"].Value.Trim() : null;
    }

    private string? BuildLoginCacheKey(HttpContext ctx, string sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return null;

        var remoteAddr = ctx.Connection.RemoteIpAddress?.ToString();
        var remotePort = ctx.Connection.RemotePort.ToString();

        // Optionally trust reverse-proxy headers
        if (_opt.TrustXRealHeaders)
        {
            if (ctx.Request.Headers.TryGetValue("X-Real-IP", out var xrip) && !string.IsNullOrWhiteSpace(xrip))
                remoteAddr = xrip.ToString();

            if (ctx.Request.Headers.TryGetValue("X-Real-Port", out var xrport) && !string.IsNullOrWhiteSpace(xrport))
                remotePort = xrport.ToString();
        }

        if (string.IsNullOrWhiteSpace(remoteAddr))
            return null;
        if (string.IsNullOrWhiteSpace(remotePort))
            remotePort = "443"; // Default to 443 if missing

        return $"{nameof(QnapAuthMiddleware)}:login:{remoteAddr}:{remotePort}/{sid}";
    }

    private static string BuildAuthCacheKey(string sid)
        => $"{nameof(QnapAuthMiddleware)}:auth:{sid}";

    /// <summary>
    /// Writes a debug message to the console if debug logging is enabled via QNAP_AUTH_DEBUG=1.
    /// The console output ends up in the service log file, so it can be toggled on the device if needed.
    /// All per-request messages must go through this method so the log stays quiet by default.
    /// </summary>
    private void LogDebug(string id, string message, Exception? ex = null)
    {
        if (_opt.DebugLogging)
            Console.WriteLine($"[QnapAuth] {id}: {message}{(ex == null ? "" : Environment.NewLine + ex)}");
    }

    /// <summary>
    /// Writes a warning message to the console, ending up in the service log file.
    /// Warnings are reserved for misconfiguration and rate-limited operational problems;
    /// never call this on a per-request path, as it would fill up the log.
    /// </summary>
    private static void LogWarning(string id, string message, Exception? ex = null)
        => Console.WriteLine($"[QnapAuth] WARNING {id}: {message}{(ex == null ? "" : Environment.NewLine + ex)}");

    /// <summary>
    /// Masks a session id for logging, keeping only a short prefix for correlation.
    /// </summary>
    private static string MaskSid(string? sid)
        => string.IsNullOrEmpty(sid)
            ? "<none>"
            : sid.Length <= 4 ? "****" : sid.Substring(0, 4) + "...(" + sid.Length + " chars)";

    /// <summary>
    /// Truncates a string for logging.
    /// </summary>
    private static string Truncate(string? value, int max = 2000)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= max ? value : value.Substring(0, max) + "...";
}

public static class QnapAuthMiddlewareExtensions
{
    /// <summary>
    /// Add QTS-integrated auth. Protects /api,/notifications by default.
    /// Env vars:
    ///   QNAP_AUTH_LOGIN_URL, QNAP_SID_COOKIE, QNAP_ALL_USERS, QNAP_AUTH_ENABLED,
    ///   QNAP_AUTH_DEBUG, QNAP_TRUST_XREAL, QNAP_PROTECT_PREFIXES,
    ///   QNAP_USERNAME, QNAP_IS_ADMIN
    /// </summary>
    public static IApplicationBuilder UseQnapAuthIfEnabled(this IApplicationBuilder app)
    {
        var opt = new QnapAuthOptions();
        if (!opt.Enabled)
            return app;

        // Misconfiguration is reported once at startup, never per request
        foreach (var problem in opt.Validate())
            Console.WriteLine($"[QnapAuth] WARNING misconfiguration: {problem}");

        Console.WriteLine("Enabling QNAP QTS authentication middleware");

        return app.UseMiddleware<QnapAuthMiddleware>(opt);
    }
}
