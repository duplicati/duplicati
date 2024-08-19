using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Extensions;
using Duplicati.WebserverCore.Middlewares;
using Duplicati.WebserverCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Configuration.Json;

namespace Duplicati.WebserverCore;

public partial class DuplicatiWebserver
{
    public IConfiguration Configuration { get; private set; }

    public WebApplication App { get; private set; }

    public IServiceProvider Provider { get; private set; }

    public int Port { get; private set; }

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
        IEnumerable<string> AllowedHostnames
    );

    public void InitWebServer(InitSettings settings, Connection connection)
    {
        Port = settings.Port;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions()
        {
            ContentRootPath = settings.WebRoot,
            WebRootPath = settings.WebRoot
        });

        // Remove all appsettings sources as they are not used, but they do install FS watchers by default
        while (true)
        {
            var appCfgSource = builder.Configuration.Sources.FirstOrDefault(x => x is JsonConfigurationSource { ReloadOnChange: true });
            if (appCfgSource == null)
                break;
            builder.Configuration.Sources.Remove(appCfgSource);
        }

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

        // Generate JWTConfig with signing key if not present
        if (string.IsNullOrWhiteSpace(connection.ApplicationSettings.JWTConfig))
            connection.ApplicationSettings.JWTConfig = JsonSerializer.Serialize(JWTConfig.Create());

        var jwtConfig = JsonSerializer.Deserialize<JWTConfig>(connection.ApplicationSettings.JWTConfig)
            ?? throw new Exception("Failed to deserialize JWTConfig");

        builder.Services
            .AddHostedService<ApplicationPartsLogger>()
            .AddEndpointsApiExplorer()
            .AddSwaggerGen()
            .AddHttpContextAccessor()
            .AddSingleton<IHostnameValidator>(new HostnameValidator(settings.AllowedHostnames))
            .AddSingleton(jwtConfig)
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = jwtConfig.Authority;
                options.Audience = jwtConfig.Audience;
                options.SaveToken = true;
                options.TokenValidationParameters = JWTTokenProvider.GetTokenValidationParameters(jwtConfig);

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["token"];

                        // If the request is for our hub...
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) &&
                            path.StartsWithSegments("/notifications"))
                        {
                            // Read the token out of the query string
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var store = context.HttpContext.RequestServices.GetRequiredService<ITokenFamilyStore>();
                        return JWTTokenProvider.ValidateAccessToken(context, store);
                    }
                };
            });

        builder.Services.AddDuplicati(connection);

        // Prevent logs from spamming the console, but allow enabling for debugging
        if (Environment.GetEnvironmentVariable("DUPLICATI_WEBSERVER_LOGGING") != "1")
        {
            builder.Logging
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager", LogLevel.Error)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("Duplicati", LogLevel.Warning);
        }

        Configuration = builder.Configuration;
        App = builder.Build();
        Provider = App.Services;

        App.UseDefaultStaticFiles(settings.WebRoot);

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
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { Error = userReportedHttpException.Message, Code = userReportedHttpException.StatusCode }));
                }
                else
                {
                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { Error = "An error occurred", Code = 500 }));
                }
            });
        });
    }

    public Task Start(InitSettings settings)
    {
        App.AddEndpoints()
            .UseNotifications("/notifications");

        return App.RunAsync();
    }

    public async Task Stop()
    {
        await App.StopAsync();
    }
}