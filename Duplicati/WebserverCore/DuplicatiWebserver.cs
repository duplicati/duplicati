using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Utility;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Extensions;
using Duplicati.WebserverCore.Middlewares;
using Duplicati.WebserverCore.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.OpenApi.Models;

namespace Duplicati.WebserverCore;

public partial class DuplicatiWebserver
{
    public IConfiguration Configuration { get; private set; }

    public WebApplication App { get; private set; }

    public IServiceProvider Provider { get; private set; }

    public int Port { get; private set; }

    public Task TerminationTask { get; private set; } = Task.CompletedTask;

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

        builder.Services.Configure<JsonOptions>(opt =>
        {
            opt.SerializerOptions.PropertyNamingPolicy = null;
            opt.SerializerOptions.Converters.Add(new DayOfWeekStringEnumConverter());
            opt.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        // Generate JWTConfig with signing key if not present
        if (string.IsNullOrWhiteSpace(connection.ApplicationSettings.JWTConfig))
            connection.ApplicationSettings.JWTConfig = JsonSerializer.Serialize(JWTConfig.Create());

        var jwtConfig = JsonSerializer.Deserialize<JWTConfig>(connection.ApplicationSettings.JWTConfig)
            ?? throw new Exception("Failed to deserialize JWTConfig");

        builder.Services
#if DEBUG
            .AddEndpointsApiExplorer()
            .AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Duplicati API Documentation", Version = "v1" });
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer"
                });
            })
#endif
            .AddHttpContextAccessor()
            .AddSingleton<IHostnameValidator>(new HostnameValidator(settings.AllowedHostnames))
            .AddSingleton(jwtConfig)
            .AddAuthorization()
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

        builder.Services.AddHttpClient();

        Configuration = builder.Configuration;
        App = builder.Build();
        Provider = App.Services;

        HttpClientHelper.Configure(App.Services.GetRequiredService<IHttpClientFactory>());

        App.UseAuthentication();
        App.UseAuthorization();

#if DEBUG
        App.UseSwagger();
        App.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Duplicati");
        });
#endif

        App.UseDefaultStaticFiles(settings.WebRoot);

        App.UseExceptionHandler(app =>
        {
            app.Run(async context =>
            {
                var thrownException = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
                if (thrownException is UserReportedHttpException userReportedHttpException)
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

        return TerminationTask = App.RunAsync();
    }

    public async Task Stop()
    {
        await App.StopAsync();
    }
}