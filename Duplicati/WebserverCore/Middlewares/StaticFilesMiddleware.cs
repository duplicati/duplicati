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
using System.Text;
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

    private sealed record SpaConfig(string Prefix, byte[] IndexFile, string BasePath);

    private static byte[] ReadAndPatchIndexFile(FileInfo file, string prefix)
    {
        if (!prefix.EndsWith("/"))
            prefix += "/";

        var index = File.ReadAllBytes(file.FullName);
        var indexStr = Encoding.UTF8.GetString(index);
        indexStr = indexStr.Replace("<base href=\"/\">", $"<base href=\"{prefix}\">");
        return Encoding.UTF8.GetBytes(indexStr);
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

        foreach (var prefix in spaPaths)
        {
            var basepath = Path.Combine(webroot, prefix.TrimStart('/'));
            if (!Directory.Exists(basepath))
                continue;

            var file = Path.Combine(basepath, "index.html");
            var fi = new FileInfo(file);
            if (fi.Exists)
            {
                prefixHandlerMap.Add(new SpaConfig(prefix, ReadAndPatchIndexFile(fi, prefix), basepath));
            }
#if DEBUG            
            else
            {
                // Install from NPM in debug mode for easier development
                var spaConfig = NpmSpaHelper.ProbeForNpmSpa(basepath);
                if (spaConfig != null)
                    prefixHandlerMap.Add(new SpaConfig(prefix, ReadAndPatchIndexFile(spaConfig.IndexFile, prefix), spaConfig.BasePath));
                else if (spaConfig == null && missingFile.Exists)
                    prefixHandlerMap.Add(new SpaConfig(prefix, File.ReadAllBytes(missingFile.FullName), basepath));
            }
#endif
        }

        if (prefixHandlerMap.Any())
        {
            prefixHandlerMap = prefixHandlerMap.OrderByDescending(p => p.Prefix.Length).ToList();
            app.Use(async (context, next) =>
            {
                await next();

                // Not found only
                if (context.Response.StatusCode != 404 || context.Response.HasStarted)
                    return;

                // Check if we can use the path
                var path = context.Request.Path;
                if (!path.HasValue)
                    return;

                // Check if the path is a SPA path
                var spaConfig = prefixHandlerMap.FirstOrDefault(p => path.Value.StartsWith(p.Prefix));
                if (spaConfig == null)
                    return;

                if (string.IsNullOrEmpty(Path.GetExtension(path)) || path.Value.EndsWith("/index.html"))
                {
                    // Serve the index file
                    context.Response.ContentType = "text/html";
                    context.Response.StatusCode = 200;
#if DEBUG
                    await context.Response.Body.WriteAsync(ReadAndPatchIndexFile(new FileInfo(Path.Combine(spaConfig.BasePath, "index.html")), spaConfig.Prefix));
#else
                    await context.Response.Body.WriteAsync(spaConfig.IndexFile);
#endif                    
                    await context.Response.CompleteAsync();
                }
                else
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