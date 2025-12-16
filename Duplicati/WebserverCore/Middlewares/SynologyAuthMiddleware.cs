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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Duplicati.WebserverCore.Middlewares;

public sealed class SynologyDsmAuthOptions
{
    /// <summary>Path to DSM authenticate.cgi</summary>
    public string AuthenticateCgi { get; set; } = GetEnvArg("SYNO_AUTHENTICATE_CGI", "/usr/syno/synoman/webman/modules/authenticate.cgi");

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
    /// If true, enable the middleware. Controlled by SYNO_DSM_AUTH_ENABLED=1/0.
    /// </summary>
    public bool Enabled { get; set; } = GetEnvArg("SYNO_DSM_AUTH_ENABLED", "0") == "1";

    /// <summary>
    /// Cache validity per (remote ip/port, cookie, token) to avoid hitting DSM on every request.
    /// </summary>
    public TimeSpan CacheTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Prefixes to protect. Defaults to /api, /login, /logout.
    /// Set SYNO_PROTECT_PREFIXES to override (comma-separated).
    /// </summary>
    public string[] ProtectedPathPrefixes { get; set; } =
        (GetEnvArg("SYNO_PROTECT_PREFIXES") ?? "/api")
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

public sealed class SynologyDsmAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SynologyDsmAuthOptions _opt;
    private readonly ConcurrentDictionary<string, DateTime> _loginCache = new ConcurrentDictionary<string, DateTime>();
    private readonly Regex _synoTokenRegex = new Regex(@"""SynoToken""\s?\:\s?""(?<token>[^""]+)""", RegexOptions.Compiled);

    private readonly bool _fullyDisabled;

    public SynologyDsmAuthMiddleware(RequestDelegate next, SynologyDsmAuthOptions options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _opt = options ?? throw new ArgumentNullException(nameof(options));

        // Validate scripts exist; if not, disable with 503.
        if (!File.Exists(_opt.AuthenticateCgi))
            _fullyDisabled = true;
    }

    public async Task Invoke(HttpContext context)
    {
        if (_fullyDisabled)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("The system is incorrectly configured");
            return;
        }

        if (!IsProtectedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Build env for DSM scripts
        var env = BuildEnv(context);

        // Cookie "id" is required for authenticate.cgi
        var loginId = context.Request.Cookies.TryGetValue("id", out var idVal) ? idVal : null;
        if (!string.IsNullOrWhiteSpace(loginId))
            env["HTTP_COOKIE"] = "id=" + loginId;


        var cacheKey = BuildCacheKey(env);

        // Authenticate
        var username = _opt.ForcedUsername;
        if (string.IsNullOrWhiteSpace(username))
        {
            try
            {
                username = await ShellExec(_opt.AuthenticateCgi, shell: false, exitcode: 0, env: env, ct: context.RequestAborted);
            }
            catch
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("The system is incorrectly configured");
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

        // Admin-only check
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
        if (cacheKey != null)
            _loginCache[cacheKey] = DateTime.Now + _opt.CacheTimeout;

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

    private static string? BuildCacheKey(Dictionary<string, string> env)
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

        return $"{addr}:{port}/{cookie}";
    }

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

            Console.WriteLine($"Executing: {psi.FileName} {psi.Arguments}");
            if (shell && args != null)
                Console.WriteLine($"Shell input: {args}");
            if (env != null)
                Console.WriteLine($"Environment: {string.Join(", ", env.Select(kv => $"{kv.Key}={kv.Value}"))}");

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

            Console.WriteLine($"Process exited with code {p.ExitCode}");
            if (!string.IsNullOrEmpty(stderr))
                Console.WriteLine($"stderr: {stderr}");
            if (!string.IsNullOrEmpty(stdout))
                Console.WriteLine($"stdout: {stdout}");

            return stdout;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ShellExec exception: {ex}");
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
