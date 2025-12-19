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
using System.Text.RegularExpressions;
using Duplicati.Library.AutoUpdater;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Net.Http.Headers;

namespace Duplicati.WebserverCore.Middlewares;

public static class StaticFilesExtensions
{
    private static readonly HashSet<string> _nonCachePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/index.html",
        "/login.html",
        "/signin.html"
    };

    private static readonly CacheControlHeaderValue _noCache = new CacheControlHeaderValue
    {
        NoCache = true,
        NoStore = true,
        MustRevalidate = true
    };

    private static readonly CacheControlHeaderValue _allowCache = new CacheControlHeaderValue
    {
        Public = true,
        MaxAge = TimeSpan.FromDays(7)
    };

    private const string FORWARDED_PREFIX_HEADER = "X-Forwarded-Prefix";
    private const string FORWARDED_PREFIX_HEADER_ALT = "X-Forwarded-Prefix-Alt";
    private const string FORWARDED_PREFIX_HEADER_NGCLIENT = "X-Forwarded-Prefix-Ngclient";
    private const string ENABLE_IFRAME_HOSTING_HEADER = "X-Allow-Iframe-Hosting";
    private const string ENABLE_IFRAME_HOSTING_ENVIRONMENT_VARIABLE = "DUPLICATI_ENABLE_IFRAME_HOSTING";
    private const string XSRF_FORWARDING_CONFIG_ENVIRONMENT_VARIABLE = "DUPLICATI_XSRF_FORWARDING_CONFIG";
    private const string NGCLIENT_LOCATION = "ngclient/";

    private sealed record SpaConfig(string Prefix, string FileContent, string BasePath);

    private static readonly Regex _baseHrefRegex = new Regex(
        @"(<base\b[^>]*?\bhref\s*=\s*)(['""])\s*[^'""]*\s*(?=\2)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _headInjectRegex = new Regex(
        @"(</head\s*>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static string PatchIndexContent(string fileContent, string prefix, string ngclientPrefix, bool enableIframeHosting, (string HeaderName, string QueryName)? xsrfForwardingConfig)
    {
        if (!prefix.EndsWith("/"))
            prefix += "/";

        if (string.IsNullOrWhiteSpace(ngclientPrefix))
            ngclientPrefix = $"{prefix}{NGCLIENT_LOCATION}";

        var headContent = string.Empty;
        if (!string.IsNullOrWhiteSpace(AutoUpdateSettings.CustomCssFilePath))
            headContent += $"<link rel=\"stylesheet\" href=\"oem-custom.css\" />";
        if (!string.IsNullOrWhiteSpace(AutoUpdateSettings.CustomJsFilePath))
            headContent += $"<script src=\"oem-custom.js\"></script>";
        if (!string.IsNullOrWhiteSpace(prefix))
            headContent += $@"<meta name=""duplicati-proxy-config"" content=""{prefix}""/> ";
        if (enableIframeHosting)
            headContent += $@"<meta name=""duplicati-enable-iframe-hosting"" content=""true""/> ";
        if (xsrfForwardingConfig is not null)
            headContent += $@"<meta name=""duplicati-xsrf-config"" data-header-name=""{xsrfForwardingConfig.Value.HeaderName}"" data-query-name=""{xsrfForwardingConfig.Value.QueryName}"" /> ";

        fileContent = _headInjectRegex.Replace(fileContent, headContent + "</head>");

        return _baseHrefRegex.Replace(fileContent, match =>
        {
            var quote = match.Groups[2].Value;
            return $"{match.Groups[1].Value}{quote}{ngclientPrefix}";
        });
    }

    public static IApplicationBuilder UseDefaultStaticFiles(this WebApplication app, string webroot, IEnumerable<string> spaPaths)
    {
        var fileTypeMappings = new FileExtensionContentTypeProvider()
        {
            Mappings = {
                    ["htc"] = "text/x-component",
                    ["json"] = "application/json",
                    ["map"] = "application/json",
                    ["htm"] = "text/html; charset=utf-8",
                    ["html"] = "text/html; charset=utf-8",
                    ["hbs"] = "application/x-handlebars-template",
                    ["woff"] = "application/font-woff",
                    ["woff2"] = "application/font-woff"
                }
        };

        var prefixHandlerMap = new List<SpaConfig>();
        var missingFile = new FileInfo(Path.Combine(webroot, "missing-spa.html"));

        var customCssFile = string.IsNullOrWhiteSpace(AutoUpdateSettings.CustomCssFilePath)
            ? null
            : new FileInfo(AutoUpdateSettings.CustomCssFilePath!);
        var customJsFile = string.IsNullOrWhiteSpace(AutoUpdateSettings.CustomJsFilePath)
            ? null
            : new FileInfo(AutoUpdateSettings.CustomJsFilePath!);

        foreach (var prefix in spaPaths)
        {
            var basepath = Path.Combine(webroot, prefix.TrimStart('/'));
            if (!Directory.Exists(basepath))
                continue;

            var file = Path.Combine(basepath, "index.html");
            var fi = new FileInfo(file);
            if (fi.Exists)
            {
                prefixHandlerMap.Add(new SpaConfig(prefix, File.ReadAllText(fi.FullName), basepath));
            }
#if DEBUG
            else
            {
                // Install from NPM in debug mode for easier development
                var spaConfig = NpmSpaHelper.ProbeForNpmSpa(basepath);
                if (spaConfig != null)
                    prefixHandlerMap.Add(new SpaConfig(prefix, File.ReadAllText(spaConfig.IndexFile.FullName), spaConfig.BasePath));
                else if (spaConfig == null && missingFile.Exists)
                    prefixHandlerMap.Add(new SpaConfig(prefix, File.ReadAllText(missingFile.FullName), basepath));
            }
#endif
        }

        if (prefixHandlerMap.Any())
        {
            prefixHandlerMap = prefixHandlerMap.OrderByDescending(p => p.Prefix.Length).ToList();
            app.Use(async (context, next) =>
            {
                // Check if the path is a SPA path
                var path = context.Request.Path;
                var spaConfig = path.HasValue ? prefixHandlerMap.FirstOrDefault(p => path.Value.StartsWith(p.Prefix)) : null;
                if (spaConfig != null && path.HasValue && (string.IsNullOrEmpty(Path.GetExtension(path)) || path.Value.EndsWith("/index.html")))
                {
                    // Serve the index file
                    context.Response.ContentType = "text/html";
                    context.Response.StatusCode = 200;
                    var indexContent = spaConfig.FileContent;
#if DEBUG
                    // In debug mode, we re-read the index file to ensure we have the latest content for debugging
                    indexContent = File.ReadAllText(Path.Combine(spaConfig.BasePath, "index.html"));
#endif
                    var forwardedPrefix = context.Request.Headers[FORWARDED_PREFIX_HEADER].FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(forwardedPrefix))
                        forwardedPrefix = context.Request.Headers[FORWARDED_PREFIX_HEADER_ALT].FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(forwardedPrefix))
                        forwardedPrefix = "";

                    var ngclientPrefix = context.Request.Headers[FORWARDED_PREFIX_HEADER_NGCLIENT].FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(ngclientPrefix))
                        ngclientPrefix = "";

                    var enableIframeHosting = Library.Utility.Utility.ParseBool(context.Request.Headers[ENABLE_IFRAME_HOSTING_HEADER].FirstOrDefault(), false)
                        || Library.Utility.Utility.ParseBool(Environment.GetEnvironmentVariable(ENABLE_IFRAME_HOSTING_ENVIRONMENT_VARIABLE), false);

                    var xsrfForwardingConfig = GetXsrfForwardingConfig();

                    await context.Response.WriteAsync(PatchIndexContent(indexContent, forwardedPrefix, ngclientPrefix, enableIframeHosting, xsrfForwardingConfig), context.RequestAborted);
                    await context.Response.CompleteAsync();
                    return;
                }

                await next();

                // Handle not found only
                if (context.Response.StatusCode != 404 || context.Response.HasStarted)
                    return;

                // Check if we can use the path
                if (!path.HasValue)
                    return;

                if (path.Value.EndsWith("/oem-custom.css") && (customCssFile?.Exists ?? false))
                {
                    // Serve the custom CSS file
                    context.Response.ContentType = "text/css";
                    context.Response.StatusCode = 200;
                    await context.Response.SendFileAsync(new PhysicalFileInfo(customCssFile));
                    await context.Response.CompleteAsync();
                }
                else if (path.Value.EndsWith("/oem-custom.js") && (customJsFile?.Exists ?? false))
                {
                    // Serve the custom JS file
                    context.Response.ContentType = "application/javascript";
                    context.Response.StatusCode = 200;
                    await context.Response.SendFileAsync(new PhysicalFileInfo(customJsFile));
                    await context.Response.CompleteAsync();
                }
                else if (spaConfig != null)
                {
                    // Serve the static file
                    var file = new FileInfo(Path.Combine(spaConfig.BasePath, path.Value.Substring(spaConfig.Prefix.Length).TrimStart('/')));
                    if (file.FullName.StartsWith(spaConfig.BasePath, Library.Utility.Utility.ClientFilenameStringComparison) && file.Exists && fileTypeMappings.Mappings.TryGetValue(Path.GetExtension(file.Extension), out var contentType))
                    {
                        context.Response.ContentType = contentType;
                        context.Response.StatusCode = 200;
                        await context.Response.SendFileAsync(new PhysicalFileInfo(file));
                        await context.Response.CompleteAsync();
                    }
                }
            });
        }

        var fileProvider = new PhysicalFileProvider(Path.GetFullPath(webroot));
        var defaultFiles = GetDefaultFiles(fileProvider);
        app.UseDefaultFiles(defaultFiles);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            RequestPath = "",
            OnPrepareResponse = (context) =>
            {
                var headers = context.Context.Response.GetTypedHeaders();
                var path = context.Context.Request.Path.Value ?? string.Empty;
                headers.CacheControl =
                                (path.EndsWith("/index.html") || _nonCachePaths.Contains(path))
                                    ? _noCache
                                    : _allowCache;
            },
            ContentTypeProvider = fileTypeMappings
        });

        return app;
    }

    private static (string HeaderName, string QueryName)? GetXsrfForwardingConfig()
    {
        var configStr = Environment.GetEnvironmentVariable(XSRF_FORWARDING_CONFIG_ENVIRONMENT_VARIABLE);
        if (string.IsNullOrWhiteSpace(configStr))
            return null;

        var parts = configStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return null;

        return (parts[0], parts[1]);
    }

    private static DefaultFilesOptions GetDefaultFiles(PhysicalFileProvider fileProvider)
    {
        var defaultFiles = new DefaultFilesOptions
        {
            FileProvider = fileProvider
        };
        defaultFiles.DefaultFileNames.Clear();
        defaultFiles.DefaultFileNames.Add("index.html");
        return defaultFiles;
    }
}
