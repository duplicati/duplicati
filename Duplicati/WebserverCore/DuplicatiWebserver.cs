using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.FileProviders;

namespace Duplicati.WebserverCore
{
    public class DuplicatiWebserver
    {
        public Action Foo()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Host.UseRESTHandlers();
            builder.Services.AddControllers()
                // This app gets launched by a different assembly, so we need to tell it to look in this one
                .AddApplicationPart(this.GetType().Assembly);
            builder.Services.AddHostedService<ApplicationPartsLogger>();
            var app = builder.Build();

            //app.UseTestMiddleware();
            //app.UseRESTHandlerEndpoints();



            string webroot = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string install_webroot = System.IO.Path.Combine(Library.AutoUpdater.UpdaterManager.InstalledBaseDir, "webroot");
            webroot = System.IO.Path.Combine(webroot, "webroot");
            var webroot_fileprovider = new PhysicalFileProvider(webroot);

            // index.html
            DefaultFilesOptions defaultFilesOptions = new DefaultFilesOptions();
            defaultFilesOptions.DefaultFileNames.Clear();
            defaultFilesOptions.DefaultFileNames.Add("index.html");
            defaultFilesOptions.FileProvider = webroot_fileprovider;
            app.UseDefaultFiles(defaultFilesOptions);

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = webroot_fileprovider,
                RequestPath = ""
            });

            app.MapControllers();

            // Test http://127.0.0.1:3001/api/v1/Licenses
            app.RunAsync("http://localhost:3001");
            return () => { app.StopAsync(); };
        }

        public Action Bar()
        {
            var builder = Host.CreateDefaultBuilder();
            builder.UseRESTHandlers();
            var app = builder.Build();

            app.RunAsync();
            return () => { app.StopAsync(); };
        }
    }

    //Useful for debugging ASP.net magically loading controllers
    public class ApplicationPartsLogger : IHostedService
    {
        private readonly ILogger<ApplicationPartsLogger> _logger;
        private readonly ApplicationPartManager _partManager;

        public ApplicationPartsLogger(ILogger<ApplicationPartsLogger> logger, ApplicationPartManager partManager)
        {
            _logger = logger;
            _partManager = partManager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Get the names of all the application parts. This is the short assembly name for AssemblyParts
            var applicationParts = _partManager.ApplicationParts.Select(x => x.Name);

            // Create a controller feature, and populate it from the application parts
            var controllerFeature = new ControllerFeature();
            _partManager.PopulateFeature(controllerFeature);

            // Get the names of all of the controllers
            var controllers = controllerFeature.Controllers.Select(x => x.Name);

            // Log the application parts and controllers
            _logger.LogInformation("Found the following application parts: '{ApplicationParts}' with the following controllers: '{Controllers}'",
                string.Join(", ", applicationParts), string.Join(", ", controllers));

            return Task.CompletedTask;
        }

        // Required by the interface
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}