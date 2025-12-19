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

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Duplicati.WebserverCore.Middlewares;

public sealed class SynologyDsmAuthOptions
{
    /// <summary>
    /// Path to DSM authenticate.cgi
    /// </summary>
    public string AuthenticateCgi { get; set; } = GetEnvArg("SYNO_AUTHENTICATE_CGI", "/usr/syno/synoman/webman/authenticate.cgi");

    /// <summary>
    /// Path to DSM login.cgi
    /// </summary>
    public string LoginCgi { get; set; } = GetEnvArg("SYNO_LOGIN_CGI", "/usr/syno/synoman/webman/login.cgi");

    /// <summary>
    /// If set, use this username instead of invoking authenticate.cgi (mostly for testing).
    /// </summary>
    public string? ForcedUsername { get; set; } = GetEnvArg("SYNO_USERNAME");

    /// <summary>
    /// If set, use these group ids (space separated) instead of invoking 'id -G' (mostly for testing).
    /// </summary>
    public string? ForcedGroupIds { get; set; } = GetEnvArg("SYNO_GROUP_IDS");

    /// <summary>
    /// Allow all DSM users (not only admins) if SYNO_ALL_USERS=1; otherwise admin-only.
    /// </summary>
    public bool AdminOnly { get; set; } = !(GetEnvArg("SYNO_ALL_USERS", "0") == "1");

    /// <summary>
    /// A flag indicating if the XSRF token should be fetched automatically
    /// </summary>
    /// <remarks>Enabling this disables XSRF protection, so use with caution.</remarks>
    public readonly bool AutoXsrf = GetEnvArg("SYNO_AUTO_XSRF", "0") == "1";

    /// <summary>
    /// If true, enable the middleware. Controlled by SYNO_DSM_AUTH_ENABLED=1/0.
    /// </summary>
    public bool Enabled { get; set; } = GetEnvArg("SYNO_DSM_AUTH_ENABLED", "0") == "1";

    /// <summary>
    /// Cache validity per (remote ip/port, cookie, token) to avoid hitting DSM on every request.
    /// </summary>
    public TimeSpan LoginCacheTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cache validity per login id cookie to avoid hitting DSM on every request.
    /// </summary>
    public TimeSpan AuthCacheTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Prefixes to protect. Defaults to /api, /login, /logout.
    /// Set SYNO_PROTECT_PREFIXES to override (comma-separated).
    /// </summary>
    public string[] ProtectedPathPrefixes { get; set; } =
        (GetEnvArg("SYNO_PROTECT_PREFIXES") ?? "/api,/notfications")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();

    /// <summary>
    /// If true, trust X-Real-IP / X-Real-Port headers (set by reverse proxy).
    /// Controlled by SYNO_TRUST_XREAL=1/0.
    /// </summary>
    public bool TrustXRealHeaders { get; set; } = GetEnvArg("SYNO_TRUST_XREAL", "1") == "1";

    /// <summary>
    /// Treat gid 101 as administrators (DSM default). If you want to avoid hardcoding,
    /// set SYNO_ADMIN_GID to a specific value or set SYNO_ADMIN_GROUP_NAME.
    /// </summary>
    public string AdminGid { get; set; } = GetEnvArg("SYNO_ADMIN_GID", "101");

    /// <summary>
    /// If set (e.g. "administrators"), checks membership by group name using 'id -Gn'.
    /// Preferred over hardcoding gid when available.
    /// </summary>
    public string AdminGroupName { get; set; } = GetEnvArg("SYNO_ADMIN_GROUP_NAME", "administrators");

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
}

/// <summary>
/// Middleware for Synology DSM integrated authentication.
/// </summary>
/// <remarks>
/// The DSM on Synology NAS devices uses CGI scripts to handle authentication and authorization.
/// This middleware integrates with those scripts to authenticate users based on DSM's user management.
/// It checks for a valid login session using the "id" cookie and optionally an XSRF token.
/// The middleware works by forwarding requests to the DSM's authenticate.cgi script to verify user credentials.
/// If the user is authenticated, the request proceeds; otherwise, a 401 Unauthorized response is returned.
/// For endpoints that involve non-static content (i.e., API calls), the middleware ensures the user is authenticated, including the XSRF token.
/// For endpoints that involve static content (i.e., html, js, css), the middleware checks that the user has a valid cookie, but does not require the XSRF token.
/// To avoid slowdowns, the middleware caches authentication results for a short period of time.
/// </remarks>  
public sealed class SynologyDsmAuthMiddleware
{
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<SynologyDsmAuthMiddleware>();
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly SynologyDsmAuthOptions _opt;
    private readonly Regex _synoTokenRegex = new Regex(@"""SynoToken""\s?\:\s?""(?<token>[^""]+)""", RegexOptions.Compiled);

    private readonly bool _fullyDisabled;

    public SynologyDsmAuthMiddleware(RequestDelegate next, IMemoryCache cache, SynologyDsmAuthOptions options)
    {
        // WARNING: This module is written for Duplicati which does not have a concept of "users" or "roles".
        // If the code is adapted for other uses, care must be taken to ensure that user identities and roles
        // are handled securely and appropriately. Specifically, the caching mechanism needs to be revised to
        // support multiple users and roles.

        _next = next ?? throw new ArgumentNullException(nameof(next));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _opt = options ?? throw new ArgumentNullException(nameof(options));

        // Validate scripts exist; if not, disable with 503.
        if (!File.Exists(_opt.AuthenticateCgi) || !File.Exists(_opt.LoginCgi))
            _fullyDisabled = true;
    }

    public async Task Invoke(HttpContext context)
    {
        // If scripts are missing, return 503, the system is not looking as expected
        if (_fullyDisabled)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("The system is incorrectly configured");
            return;
        }

        // Check if this is an API call, or a static content call
        var isApiCall = IsProtectedPath(context.Request.Path);

        // Static content calls are GET or HEAD requests to non-API paths
        var isStaticContentCall = !isApiCall && new[] { "GET", "HEAD" }.Contains(context.Request.Method.ToUpperInvariant());

        // Cookie "id" is required for authenticate.cgi
        var loginId = context.Request.Cookies.TryGetValue("id", out var idVal) ? idVal : null;

        // Static content calls require a valid cookie, but does not (usually) have an XSRF token,
        // so we cannot call authenticate.cgi, but instead check if the token itself has been authenticated recently
        // giving "authenticated but no XSRF protection" for static content
        if (isStaticContentCall)
        {
            if (!string.IsNullOrWhiteSpace(loginId) && _cache.TryGetValue(BuildAuthCacheKey(loginId), out _))
            {
                // Authenticated recently, allow
                await _next(context);
                return;
            }
        }

        // Build env for DSM scripts, prepare for invocation of CGI scripts
        var env = BuildEnv(context);
        if (!string.IsNullOrWhiteSpace(loginId))
            env["HTTP_COOKIE"] = "id=" + loginId;

        // Get XSRF token from header or query
        // API calls supply a header, but some calls (like static content or websockets) use query
        string? xsrftoken = context.Request.Headers["X-Syno-Token"];
        if (string.IsNullOrWhiteSpace(xsrftoken) && context.Request.Query.TryGetValue("SynoToken", out var qt))
            xsrftoken = qt.ToString();

        // Build cache key for the login from this specific client
        var loginCacheKey = BuildLoginCacheKey(env, xsrftoken);

        // Check cache so we do not need to call authenticate.cgi for every request
        if (!string.IsNullOrWhiteSpace(loginCacheKey) && _cache.TryGetValue(loginCacheKey, out _))
        {
            await _next(context);
            return;
        }

        // Auto-fetch happens when AutoXsrf is enabled or when serving static content
        // It is possible to enable this for API requests as well by environment variable, but that is not recommended for security reasons
        // If we get a request for static content, we auto-fetch the token to allow static content to load (still protected by cookie)
        if (string.IsNullOrWhiteSpace(xsrftoken) && (_opt.AutoXsrf || isStaticContentCall))
        {
            try
            {
                // Call login.cgi to get XSRF token
                var resp = ShellExec(_opt.LoginCgi, env: env).Result;

                var m = _synoTokenRegex.Match(resp);
                if (m.Success)
                    xsrftoken = m.Groups["token"].Value;
                else
                    throw new Exception("Unable to get XSRF token");
            }
            catch (Exception)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Request not authorized");
                return;
            }
        }

        // Include the XSRF token if available
        if (!string.IsNullOrWhiteSpace(xsrftoken))
            env["HTTP_X_SYNO_TOKEN"] = xsrftoken;

        // Authenticate
        var username = _opt.ForcedUsername;
        if (string.IsNullOrWhiteSpace(username))
        {
            try
            {
                // Call authenticate.cgi to get username
                username = await ShellExec(_opt.AuthenticateCgi, shell: false, exitcode: 0, env: env, ct: context.RequestAborted);
            }
            catch
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Request not authorized");
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Permission denied, not logged in");
            return;
        }

        username = username.Trim();

        // Admin-only check, look up group membership
        if (_opt.AdminOnly)
        {
            var isAdmin = await IsAdminUser(username, context.RequestAborted);
            if (!isAdmin)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Administrator login required");
                return;
            }
        }

        // Auth OK: cache (only if cacheKey available)
        if (!string.IsNullOrWhiteSpace(loginCacheKey))
            _cache.Set(loginCacheKey, true, _opt.LoginCacheTimeout);

        // Auth OK: cache loginId too
        if (!string.IsNullOrWhiteSpace(loginId))
            _cache.Set(BuildAuthCacheKey(loginId), true, _opt.AuthCacheTimeout);
        await _next(context);
    }

    private bool IsProtectedPath(PathString path)
    {
        var p = path.Value ?? string.Empty;
        return _opt.ProtectedPathPrefixes.Any(prefix =>
            p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private Dictionary<string, string> BuildEnv(HttpContext ctx)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Defaults from actual connection
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

        if (!string.IsNullOrWhiteSpace(remoteAddr))
            env["REMOTE_ADDR"] = remoteAddr;
        if (!string.IsNullOrWhiteSpace(remotePort))
            env["REMOTE_PORT"] = remotePort;
        else
            env["REMOTE_PORT"] = "443"; // Default to 443 if missing

        // Setup other required env vars, not really used, but needed by DSM scripts
        env["SERVER_ADDR"] = "127.0.0.1";
        env["REQUEST_METHOD"] = "GET";
        env["SERVER_PROTOCOL"] = "HTTP/1.1";
        env["HTTP_HOST"] = ctx.Request.Host.Host;

        return env;
    }

    private static string? BuildLoginCacheKey(Dictionary<string, string> env, string? xsrfToken)
    {
        if (env == null)
            return null;

        if (!env.TryGetValue("REMOTE_ADDR", out var addr) ||
            !env.TryGetValue("REMOTE_PORT", out var port) ||
            !env.TryGetValue("HTTP_COOKIE", out var cookie) ||
            string.IsNullOrWhiteSpace(addr) ||
            string.IsNullOrWhiteSpace(port) ||
            string.IsNullOrWhiteSpace(cookie))
            return null;

        return $"{nameof(SynologyDsmAuthMiddleware)}:login:{addr}:{port}/{cookie}?{xsrfToken}";
    }

    private static string BuildAuthCacheKey(string loginId)
        => $"{nameof(SynologyDsmAuthMiddleware)}:auth:{loginId}";

    private async Task<bool> IsAdminUser(string username, CancellationToken ct)
    {
        // 1) Prefer group-name membership (more robust).
        if (!string.IsNullOrWhiteSpace(_opt.AdminGroupName))
        {
            try
            {
                // id -Gn prints group names
                var groupsByName = await ShellExec("id", $"-Gn {EscapeArg(username)}", shell: false, exitcode: 0, env: null, ct: ct);
                var names = (groupsByName ?? string.Empty)
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (names.Any(n => string.Equals(n, _opt.AdminGroupName, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch
            {
                // Fall back to gid checks below
            }
        }

        // 2) If forced gids are provided, use them.
        var gids = _opt.ForcedGroupIds;
        if (string.IsNullOrWhiteSpace(gids))
        {
            try
            {
                // id -G prints numeric group IDs
                gids = await ShellExec("id", $"-G {EscapeArg(username)}", shell: false, exitcode: 0, env: null, ct: ct) ?? string.Empty;
                gids = gids.Replace(Environment.NewLine, string.Empty);
            }
            catch
            {
                return false;
            }
        }

        var gidList = gids.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return gidList.Contains(_opt.AdminGid);
    }

    private static string EscapeArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        // Minimal safe quoting for ProcessStartInfo.Arguments parsing.
        if (value.IndexOfAny(new[] { ' ', '\t', '"', '\\' }) >= 0)
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return value;
    }

    private static async Task<string> ShellExec(
        string command,
        string? args = null,
        bool shell = false,
        int exitcode = -1,
        Dictionary<string, string>? env = null,
        CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = shell ? null : (args ?? string.Empty),
                UseShellExecute = false,
                RedirectStandardInput = shell,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (env != null)
                foreach (var kv in env)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;

            using var p = Process.Start(psi);
            if (p == null)
                throw new Exception($"Failed to start process: {command}");

            if (shell && args != null)
                await p.StandardInput.WriteLineAsync(args);

            // Read output concurrently; avoid deadlocks.
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            // Wait with cancellation.
            while (!p.HasExited)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(100, ct);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (exitcode != -1 && p.ExitCode != exitcode)
                throw new Exception($"Exit code was: {p.ExitCode}, stdout: {stdout}, stderr: {stderr}");

            return stdout;
        }
        catch (Exception ex)
        {
            Duplicati.Library.Logging.Log.WriteErrorMessage(LOGTAG, "ShellExecError", ex, $"Error executing command '{command} {args}'");
            throw;
        }
    }
}

public static class SynologyDsmAuthMiddlewareExtensions
{
    /// <summary>
    /// Add DSM-integrated auth. Protects /api,/login,/logout by default.
    /// Env vars:
    ///   SYNO_LOGIN_CGI, SYNO_AUTHENTICATE_CGI, SYNO_ALL_USERS, SYNO_DSM_AUTH_ENABLED,
    ///   SYNO_TRUST_XREAL, SYNO_PROTECT_PREFIXES,
    ///   SYNO_USERNAME, SYNO_GROUP_IDS, SYNO_ADMIN_GID, SYNO_ADMIN_GROUP_NAME
    /// </summary>
    public static IApplicationBuilder UseSynologyDsmAuthIfEnabled(this IApplicationBuilder app)
    {
        var opt = new SynologyDsmAuthOptions();
        if (!opt.Enabled)
            return app;

        Console.WriteLine("Enabling Synology DSM authentication middleware");

        return app.UseMiddleware<SynologyDsmAuthMiddleware>(opt);
    }
}
