using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Extensions;
using Duplicati.WebserverCore.Middlewares;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Duplicati.WebserverCore;

public partial class DuplicatiWebserver
{
    public IConfiguration Configuration { get; private set; }

    public WebApplication App { get; private set; }

    public IServiceProvider Provider { get; private set; }

    public int Port => App.Configuration.GetValue("Port", 8200);

    /// <summary>
    /// The settings used for stating the server
    /// </summary>
    /// <param name="WebRoot">The root folder with static files</param>
    /// <param name="Port">The listining port</param>
    /// <param name="Interface">The listening interface</param>
    /// <param name="Certificate">The certificate, if any</param>
    /// <param name="Servername">The servername to report</param>
    public record InitSettings(
        string WebRoot,
        int Port,
        System.Net.IPAddress Interface,
        X509Certificate2? Certificate,
        string Servername,
        string Password,
        IEnumerable<string> AllowedHostnames
    );

    public void InitWebServer(InitSettings settings, Connection connection)
    {
        var builder = WebApplication.CreateBuilder();
        var allowedHostnames = settings.AllowedHostnames;
        if (allowedHostnames.Any() && allowedHostnames.Any(x => x == "*"))
            builder.WebHost.UseUrls(allowedHostnames.Select(hostname => $"{(settings.Certificate == null ? "http" : "https")}://{hostname}:{settings.Port}").ToArray());

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(settings.Interface, settings.Port, listenOptions =>
            {
                if (settings.Certificate != null)
                    listenOptions.UseHttps(settings.Certificate);
            });
        });

        //builder.Host.UseRESTHandlers();
        builder.Services.ConfigureHttpJsonOptions(opt =>
        {
            opt.SerializerOptions.PropertyNamingPolicy = null;
            opt.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            opt.SerializerOptions.Converters.Add(new DayOfWeekStringEnumConverter());
        });

        builder.Services.AddControllers()
            // This app gets launched by a different assembly, so we need to tell it to look in this one
            .AddApplicationPart(GetType().Assembly)
            .AddJsonOptions(opt =>
            {
                opt.JsonSerializerOptions.PropertyNamingPolicy = null;
                opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                opt.JsonSerializerOptions.Converters.Add(new DayOfWeekStringEnumConverter());
            });

        builder.Services
            .AddHostedService<ApplicationPartsLogger>()
            .AddEndpointsApiExplorer()
            .AddSwaggerGen()
            .AddHttpContextAccessor()
            .AddAntiforgery(options =>
            {
                options.HeaderName = "X-XSRF-TOKEN";
                options.Cookie.Name = "xsrf-token";
                options.FormFieldName = "x-xsrf-token";
            });

        builder.Services.AddDuplicati(connection);

        Configuration = builder.Configuration;
        App = builder.Build();
        Provider = App.Services;

        App.UseStaticFiles(new StaticFileOptions()
        {
            RequestPath = "",
            FileProvider = new PhysicalFileProvider(Path.GetFullPath(settings.WebRoot)),
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

        App.UseExceptionHandler(app =>
        {
            app.Run(async context =>
            {
                var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
                if (exceptionHandlerPathFeature?.Error is UserReportedHttpException userReportedHttpException)
                {
                    context.Response.StatusCode = userReportedHttpException.StatusCode;
                    context.Response.ContentType = userReportedHttpException.ContentType;
                    if (context.Response.ContentType == "text/plain")
                        await context.Response.WriteAsync(userReportedHttpException.Message);
                    else
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { Error = userReportedHttpException.Message }));
                }
                else
                {
                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { Error = "An error occurred" }));
                }
            });
        });
    }


    public void Start(InitSettings settings)
    {
        // App.UseAuthMiddleware();

        //TODO: remove
        App.MapControllers();

        App.AddEndpoints()
            .UseNotifications(settings.AllowedHostnames, "/notifications");

        App.UseAntiforgery();
        App.RunAsync();
    }

    public async Task Stop()
    {
        await App.StopAsync();
    }
}