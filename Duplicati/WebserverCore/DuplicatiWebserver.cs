// Copyright (C) 2024, The Duplicati Team
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
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;

namespace Duplicati.WebserverCore;

public partial class DuplicatiWebserver
{
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<DuplicatiWebserver>();
    public IConfiguration Configuration { get; private set; }

    public WebApplication App { get; private set; }

    public IServiceProvider Provider { get; private set; }

    public int Port { get; private set; }

    public Task TerminationTask { get; private set; } = Task.CompletedTask;

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
    /// <param name="Servername">The servername to report</param>
    /// <param name="AllowedHostnames">The allowed hostnames</param>
    /// <param name="DisableStaticFiles">If static files should be disabled</param>
    /// <param name="SPAPaths">The paths to serve as SPAs</param>
    public record InitSettings(
        string WebRoot,
        int Port,
        System.Net.IPAddress Interface,
        X509Certificate2Collection? Certificate,
        string Servername,
        IEnumerable<string> AllowedHostnames,
        bool DisableStaticFiles,
        IEnumerable<string> SPAPaths
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
            .AddAuthorization()
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = jwtConfig.Authority;
                options.Audience = jwtConfig.Audience;
                options.SaveToken = true;
                options.TokenValidationParameters = JWTTokenProvider.GetTokenValidationParameters(jwtConfig);
                options.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration();

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

        builder.Services.AddHealthChecks()
            .AddCheck("Basic", () => HealthCheckResult.Healthy("Service is running"));

        builder.Services.AddDuplicati(connection);

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

        Configuration = builder.Configuration;
        App = builder.Build();
        Provider = App.Services;

        HttpClientHelper.Configure(App.Services.GetRequiredService<IHttpClientFactory>());

        App.UseAuthentication();
        App.UseAuthorization();

        if (EnableSwagger)
        {
            App.UseSwagger();
            App.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Duplicati");
            });
        }

        if (!settings.DisableStaticFiles)
            App.UseDefaultStaticFiles(settings.WebRoot, settings.SPAPaths);

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
            App.Services.GetRequiredService<IRemoteController>().Enable();

        // Preload static system info, for better first-load experience
        var _ = Task.Run(() => App.Services.GetRequiredService<ISystemInfoProvider>().GetSystemInfo(null));
    }

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

    public Task Start(InitSettings settings)
    {
        App.MapHealthChecks("/health");
        App.AddEndpoints()
            .UseNotifications("/notifications");

        return TerminationTask = App.RunAsync();
    }

    public async Task Stop()
    {
        await App.StopAsync();
    }
}