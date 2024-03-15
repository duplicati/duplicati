using Duplicati.WebserverCore.Options;
using Microsoft.Extensions.FileProviders;

namespace Duplicati.WebserverCore.Middlewares;

public static class StaticFilesExtensions
{
    public static IApplicationBuilder UseDefaultStaticFiles(this WebApplication app, IConfiguration configuration)
    {
        var options = configuration.GetRequiredSection(StaticFilesOptions.SectionName).Get<StaticFilesOptions>()!;

        var webroot = Path.Combine(options.ContentRootPathOverride ?? app.Environment.ContentRootPath, options.Webroot);
        var fileProvider = new PhysicalFileProvider(webroot);

        var defaultFiles = GetDefaultFiles(fileProvider);
        app.UseDefaultFiles(defaultFiles);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            RequestPath = "",
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