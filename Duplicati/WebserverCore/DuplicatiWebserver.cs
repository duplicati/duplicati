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
                        var repo = context.HttpContext.RequestServices.GetRequiredService<ITokenFamilyStore>();
                        return JWTTokenProvider.ValidateAccessToken(context, repo);
                    }
                };
            });

        builder.Services.AddDuplicati(connection);

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

        App.RunAsync();
    }

    public async Task Stop()
    {
        await App.StopAsync();
    }
}