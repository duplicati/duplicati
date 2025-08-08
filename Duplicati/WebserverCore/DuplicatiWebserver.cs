// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto.V2;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Extensions;
using Duplicati.WebserverCore.Middlewares;
using Duplicati.WebserverCore.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;

namespace Duplicati.WebserverCore;

/// <summary>
/// Instance of the Duplicati webserver
/// </summary>
public class DuplicatiWebserver
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<DuplicatiWebserver>();
    /// <summary>
    /// The name of the CORS policy
    /// </summary>
    public const string CorsPolicyName = "CustomCorsPolicy";

    /// <summary>
    /// The configuration for the server
    /// </summary>
    public required IConfiguration Configuration { get; init; }

    /// <summary>
    /// The web application
    /// </summary>
    public required WebApplication App { get; init; }

    /// <summary>
    /// The service provider
    /// </summary>
    public IServiceProvider Provider => App.Services;

    /// <summary>
    /// The port the server is listening on
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// The interfaces being listened to
    /// </summary>
    public required string Interface { get; init; }

    /// <summary>
    /// The port the server is listening on
    /// </summary>
    public required bool CorsEnabled { get; init; }

    /// <summary>
    /// The task that will be set when the server is terminated
    /// </summary>
    public Task TerminationTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// If Swagger should be enabled
    /// </summary>
#if DEBUG
    private static readonly bool EnableSwagger = true;
#else
    private static readonly bool EnableSwagger = false;
#endif

    /// <summary>
    /// The settings used for stating the server
    /// </summary>
    /// <param name="WebRoot">The root folder with static files</param>
    /// <param name="Port">The listining port</param>
    /// <param name="Interface">The listening interface</param>
    /// <param name="Certificate">The certificate, if using SSL</param>
    /// <param name="AllowedHostnames">The allowed hostnames</param>
    /// <param name="DisableStaticFiles">If static files should be disabled</param>
    /// <param name="TokenLifetimeInMinutes">The lifetime of refresh tokens in minutes</param>
    /// <param name="SPAPaths">The paths to serve as SPAs</param>
    /// <param name="CorsOrigins">The origins to allow for CORS</param>
    /// <param name="PreAuthTokens">The pre-authenticated tokens</param>
    public record InitSettings(
        string WebRoot,
        int Port,
        System.Net.IPAddress Interface,
        X509Certificate2Collection? Certificate,
        IEnumerable<string> AllowedHostnames,
        bool DisableStaticFiles,
        int TokenLifetimeInMinutes,
        IEnumerable<string> SPAPaths,
        IEnumerable<string> CorsOrigins,
        IReadOnlySet<string> PreAuthTokens
    );

    /// <summary>
    /// Creates a new webserver instance
    /// </summary>
    /// <param name="settings">The settings for the server</param>
    /// <param name="connection">The connection to the database</param>
    /// <param name="logWriteHandler">The log write handler</param>
    /// <param name="applicationSettings">The application settings</param>
    /// <returns>The new webserver instance</returns>
    public static DuplicatiWebserver CreateWebServer(InitSettings settings, Connection connection, ILogWriteHandler logWriteHandler, IApplicationSettings applicationSettings)
    {
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
            // Handle IPv6 addresses
            if (settings.Interface == System.Net.IPAddress.Any)
            {
                options.ListenAnyIP(settings.Port, listenOptions =>
                {
                    if (settings.Certificate != null)
                        ConfigureHttps(listenOptions, settings.Certificate);
                });
            }
            else if (settings.Interface == System.Net.IPAddress.Loopback)
            {
                options.ListenLocalhost(settings.Port, listenOptions =>
                {
                    if (settings.Certificate != null)
                        ConfigureHttps(listenOptions, settings.Certificate);
                });
            }
            else
            {
                options.Listen(settings.Interface, settings.Port, listenOptions =>
                {
                    if (settings.Certificate != null)
                        ConfigureHttps(listenOptions, settings.Certificate);
                });
            }
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

        if (settings.TokenLifetimeInMinutes > 0)
            jwtConfig = jwtConfig with { RefreshTokenDurationInMinutes = settings.TokenLifetimeInMinutes };

        if (EnableSwagger)
        {
            // Swagger is picking up JSON settings from controllers, even though we do not use controllers
            builder.Services.AddControllers()
                .AddJsonOptions(opt =>
                {
                    opt.JsonSerializerOptions.PropertyNamingPolicy = null;
                    opt.JsonSerializerOptions.Converters.Add(new DayOfWeekStringEnumConverter());
                    opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });

            builder.Services
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
                });
        }

        builder.Services
            .AddHttpContextAccessor()
            .AddSingleton<IHostnameValidator>(new HostnameValidator(settings.AllowedHostnames))
            .AddSingleton(jwtConfig)
            .AddSingleton(new PreAuthTokenConfig(settings.PreAuthTokens))
            .AddAuthorization()
            .AddAuthentication(options => { options.DefaultScheme = "CombinedScheme"; })
            .AddScheme<AuthenticationSchemeOptions, PreAuthTokenHandler>(PreAuthTokenHandler.SchemeName, null)
            .AddPolicyScheme("CombinedScheme", $"JWT or {PreAuthTokenHandler.SchemeName}", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authHeader = context.Request.Headers["Authorization"].ToString();
                    if (authHeader?.StartsWith(PreAuthTokenHandler.SchemePrefix) == true)
                        return "PreAuth";

                    // Support for websocket authentication (should be fixed with messages)
                    var queryToken = context.Request.Query["token"].ToString();
                    if (string.IsNullOrWhiteSpace(authHeader) && !string.IsNullOrEmpty(queryToken) && settings.PreAuthTokens.Contains(queryToken))
                        return "PreAuth";

                    return "Jwt";
                };
            })
            .AddJwtBearer("Jwt", options =>
            {
                options.Authority = jwtConfig.Authority;
                options.Audience = jwtConfig.Audience;
                options.SaveToken = true;
                options.TokenValidationParameters = JWTTokenProvider.GetTokenValidationParameters(jwtConfig);
                options.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration();

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = (context) =>
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

        builder.Services.AddHealthChecks()
            .AddCheck("Basic", () => HealthCheckResult.Healthy("Service is running"));

        builder.Services.AddDuplicati(connection, logWriteHandler, applicationSettings);

        // Prevent logs from spamming the console, but allow enabling for debugging
        if (Environment.GetEnvironmentVariable("DUPLICATI_WEBSERVER_LOGGING") != "1")
        {
            builder.Logging
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager", LogLevel.Error)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("Duplicati", LogLevel.Warning)
                .AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
        }

        builder.Services.AddHttpClient();

        var useCors = settings.CorsOrigins != null && settings.CorsOrigins.Any();
        if (useCors)
        {
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(
                    name: CorsPolicyName,
                    policy =>
                    {
                        policy.WithOrigins(settings.CorsOrigins!.ToArray())
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });
            });
        }

        var app = builder.Build();
        HttpClientHelper.Configure(app.Services.GetRequiredService<IHttpClientFactory>());

        if (useCors)
            app.UseCors(CorsPolicyName);

        app.UseAuthentication();
        app.UseAuthorization();

        if (EnableSwagger)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Duplicati");
            });
        }

        if (!settings.DisableStaticFiles)
            app.UseDefaultStaticFiles(settings.WebRoot, settings.SPAPaths);

        app.Use(async (context, next) =>
        {
            // Set up the log scope to capture any log messages for the livelog / server log
            using (var scope = Library.Logging.Log.StartScope(logWriteHandler))
                await next();
        });

        app.UseExceptionHandler(app =>
        {
            app.Run(async context =>
            {
                var thrownException = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
                var userReportedHttpException = thrownException as UserReportedHttpException;
                var userInformationException = thrownException as UserInformationException;
                var isV2Request = (context.Request.Path.Value ?? "").StartsWith("/api/v2/");

                if (isV2Request && userReportedHttpException != null)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(ResponseEnvelope.Failure(userReportedHttpException.Message, $"HTTP-{userReportedHttpException.StatusCode}")));
                }
                else if (isV2Request && userInformationException != null)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(ResponseEnvelope.Failure(userInformationException.Message, userInformationException.HelpID)));
                }
                else if (userReportedHttpException != null)
                {
                    context.Response.StatusCode = userReportedHttpException.StatusCode;
                    context.Response.ContentType = userReportedHttpException.ContentType;
                    if (context.Response.ContentType == "text/plain")
                        await context.Response.WriteAsync(userReportedHttpException.Message);
                    else
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { Error = userReportedHttpException.Message, Code = userReportedHttpException.StatusCode }));
                }
                else if (userInformationException != null)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { Error = userInformationException.Message, Code = (int)HttpStatusCode.BadRequest, HelpId = userInformationException.HelpID }));
                }
                else
                {
                    // These are unexpected exceptions, so log them
                    app.ApplicationServices.GetRequiredService<ILogger<DuplicatiWebserver>>()
                        .LogError(thrownException, "Unhandled exception");

                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { Error = "An error occurred", Code = 500 }));
                }
            });
        });

        if (connection.ApplicationSettings.RemoteControlEnabled)
            app.Services.GetRequiredService<IRemoteController>().Enable();

        // Preload static system info, for better first-load experience
        _ = Task.Run(() => app.Services.GetRequiredService<ISystemInfoProvider>().GetSystemInfo(null));

        // Get a string description of the listen interface used
        var listenInterface = settings.Interface == IPAddress.Any
            ? "*"
            : settings.Interface == IPAddress.Loopback
                ? "localhost"
                : settings.Interface.ToString();

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

        // Register SIGTERM handler
        lifetime.ApplicationStopping.Register(() => applicationSettings.SignalApplicationExit());

        return new DuplicatiWebserver()
        {
            Configuration = builder.Configuration,
            App = app,
            Port = settings.Port,
            Interface = listenInterface,
            CorsEnabled = useCors
        };

    }

    /// <summary>
    /// Configures HTTPS for the server
    /// </summary>
    /// <param name="listenOptions">The listen options</param>
    /// <param name="certificates">The certificates to use</param>
    private static void ConfigureHttps(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listenOptions, X509Certificate2Collection? certificates)
    {
        var servedCert = certificates?.FirstOrDefault(x => x.HasPrivateKey)
            ?? throw new Exception("No certificate with private key found");

        listenOptions.UseHttps(new HttpsConnectionAdapterOptions()
        {
            ServerCertificate = servedCert,
            ServerCertificateChain = certificates
        });

        // This does not appear to have any effect,
        // but setting it anyway in case it is needed in the future
        listenOptions.UseHttps(new HttpsConnectionAdapterOptions()
        {
            ServerCertificate = servedCert,
            ServerCertificateChain = certificates
        });
    }

    /// <summary>
    /// Starts the webserver
    /// </summary>
    /// <returns>The task that will be set when the server is terminated</returns>
    public Task Start()
    {
        App.MapHealthChecks("/health");
        App.AddEndpoints(CorsEnabled)
            .UseNotifications("/notifications");

        return TerminationTask = App.RunAsync();
    }

    /// <summary>
    /// Stops the webserver
    /// </summary>
    /// <returns>An awaitable task</returns>
    public async Task Stop()
    {
        await App.StopAsync();
    }
}
