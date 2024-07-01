using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
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


    public static IApplicationBuilder UseDefaultStaticFiles(this WebApplication app, string webroot)
    {
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
            ContentTypeProvider = new FileExtensionContentTypeProvider()
            {
                Mappings = {
                    ["htc"] = "text/x-component",
                    ["json"] = "application/json",
                    ["map"] = "application/json",
                    ["htm"] = "text/html; charset=utf-8",
                    ["html"] = "text/html; charset=utf-8",
                    ["hbs"] = "application/x-handlebars-template",
                    ["woff"] = "application/font-woff",
                    ["woff2"] = "application/font-woff",
                }
            }
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