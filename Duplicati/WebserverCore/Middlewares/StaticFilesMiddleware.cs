using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Duplicati.WebserverCore.Middlewares;

public static class StaticFilesExtensions
{
    public static IApplicationBuilder UseDefaultStaticFiles(this WebApplication app, string webroot)
    {
        var fileProvider = new PhysicalFileProvider(Path.GetFullPath(webroot));
        var defaultFiles = GetDefaultFiles(fileProvider);
        app.UseDefaultFiles(defaultFiles);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            RequestPath = "",
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