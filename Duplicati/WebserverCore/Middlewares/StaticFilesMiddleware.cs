using System.Reflection;
using Microsoft.Extensions.FileProviders;

namespace Duplicati.WebserverCore.Middlewares;

public static class StaticFilesExtensions
{
    public static IApplicationBuilder UseDefaultStaticFiles(this IApplicationBuilder app)
    {
        var webroot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        webroot = Path.Combine(webroot, "webroot");
        var webrootFileProvider = new PhysicalFileProvider(webroot);

        var defaultFilesOptions = new DefaultFilesOptions();
        defaultFilesOptions.DefaultFileNames.Clear();
        defaultFilesOptions.DefaultFileNames.Add("index.html");
        defaultFilesOptions.FileProvider = webrootFileProvider;
        app.UseDefaultFiles(defaultFilesOptions);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = webrootFileProvider,
            RequestPath = ""
        });

        return app;
    }
}