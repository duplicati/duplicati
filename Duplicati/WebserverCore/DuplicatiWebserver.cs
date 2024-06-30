using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Extensions;
using Duplicati.WebserverCore.Middlewares;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;

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
        IEnumerable<string> AllowedHostnames
    );

    public void InitWebServer(InitSettings settings, Connection connection)
    {
        var builder = WebApplication.CreateBuilder();
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
            .AddHostFiltering(options =>
            {
                if (!settings.AllowedHostnames.Any(x => x == "*"))
                    options.AllowedHosts = settings.AllowedHostnames.ToArray();
            })
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
                    OnTokenValidated = context =>
                    {
                        var store = context.HttpContext.RequestServices.GetRequiredService<ITokenFamilyStore>();
                        return JWTTokenProvider.ValidateAccessToken(context, store);
                    }
                };
            });

        builder.Services.AddDuplicati(connection);

        // Prevent logs from spamming the console
        builder.Logging
            .AddFilter("Microsoft", LogLevel.Warning)
            .AddFilter("System", LogLevel.Warning);

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
        App.AddEndpoints();
        // Disable WebSockets until it is correctly implemented
        //     .UseNotifications(settings.AllowedHostnames, "/notifications");

        return App.RunAsync();
    }

    public async Task Stop()
    {
        await App.StopAsync();
    }
}