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

using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Reactive.Linq;
using System.Security.Cryptography;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Library.RemoteControl;
using Duplicati.Library.RestAPI;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Duplicati.Agent;

public static class Program
{
    /// <summary>
    /// The log tag for messages from this class
    /// </summary>
    private static readonly string LogTag = Log.LogTagFromType(typeof(Program));

    /// <summary>
    /// Create a random pre-shared key for the agent
    /// </summary>
    private static string? PreSharedKey = null;


    /// <summary>
    /// Commandline arguments for the agent
    /// </summary>
    /// <param name="RegistrationUrl">The server URL to connect to</param>
    /// <param name="AgentSettingsFile">The file to use for the agent settings</param>
    /// <param name="WebserviceListenInterface">The interface to listen on for the webserver</param>
    /// <param name="WebservicePort">The port to listen on for the webserver</param>
    /// <param name="WebservicePassword">The password for the webserver, random if none supplied</param>
    /// <param name="WindowsEventLog">The Windows event log to write to</param>
    /// <param name="DisableDbEncryption">Disable database encryption</param>
    /// <param name="WebserviceResetJwtConfig">Reset the JWT configuration</param>
    /// <param name="WebserviceAllowedHostnames">The allowed hostnames for the webserver</param>
    /// <param name="WebserviceApiOnly">Only allow API access to the webserver</param>
    /// <param name="WebserviceDisableSigninTokens">Disable signin tokens for the webserver</param>
    /// <param name="PingPongKeepalive">Enable ping-pong keepalive</param>
    /// <param name="DisablePreSharedKey">Disable the pre-shared key</param>
    private sealed record CommandLineArguments(
        string RegistrationUrl,
        FileInfo AgentSettingsFile,
        string WebserviceListenInterface,
        string WebservicePort,
        string WebservicePassword,
        string WindowsEventLog,
        bool DisableDbEncryption,
        bool WebserviceResetJwtConfig,
        string WebserviceAllowedHostnames,
        bool WebserviceApiOnly,
        bool WebserviceDisableSigninTokens,
        bool PingPongKeepalive,
        bool DisablePreSharedKey
    );

    [STAThread]
    public static Task<int> Main(string[] args)
    {
        Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref args, Library.AutoUpdater.PackageHelper.NamedExecutable.Agent);

        var runcmd = new Command("run", "Runs the agent")
        {
            new Option<string>("registration-url", description: "The server URL to connect to", getDefaultValue: () => RegisterForRemote.DefaultRegisterationUrl),
            new Option<FileInfo>("agent-settings-file", description: "The file to use for the agent settings", getDefaultValue: () => new FileInfo(Settings.DefaultSettingsFile)),
            new Option<string>("webservice-listen-interface", description: "The interface to listen on for the webserver", getDefaultValue: () => "loopback"),
            new Option<string>("webservice-port", description: "The port to listen on for the webserver", getDefaultValue: () => "8210"),
            new Option<string?>("webservice-password", description: "The password for the webserver, random if none supplied", getDefaultValue: () => null),
            new Option<string>("windows-eventlog", description: "The Windows event log to write to", getDefaultValue: () => "Duplicati"),
            new Option<bool>("disable-db-encryption", description: "Disable database encryption", getDefaultValue: () => false),
            new Option<bool>("webservice-reset-jwt-config", description: "Reset the JWT configuration", getDefaultValue: () => true),
            new Option<string>("webservice-allowed-hostnames", description: "The allowed hostnames for the webserver", getDefaultValue: () => "127.0.0.1"),
            new Option<bool>("webservice-api-only", description: "Only allow API access to the webserver", getDefaultValue: () => true),
            new Option<bool>("webservice-disable-signin-tokens", description: "Disable signin tokens for the webserver", getDefaultValue: () => true),
            new Option<bool>("ping-pong-keepalive", description: "Enable ping-pong keepalive", getDefaultValue: () => false)
        };
        runcmd.Handler = CommandHandler.Create<CommandLineArguments>(x => RunAgent(x, args));

        return new RootCommand("Duplicati Agent")
        {
            runcmd
        }.InvokeAsync(args);

    }

    /// <summary>
    /// Runs the agent
    /// </summary>
    /// <param name="agentConfig">The configuration from the commandline</param>
    /// <param name="args">All the arguments from the commandline</param>
    /// <returns>The exit code</returns>
    private static async Task<int> RunAgent(CommandLineArguments agentConfig, string[] args)
    {
        // Prevent access to the webserver interface from anything but the agent
        if (string.IsNullOrWhiteSpace(agentConfig.WebservicePassword))
            agentConfig = agentConfig with { WebservicePassword = System.Security.Cryptography.RandomNumberGenerator.GetHexString(128) };

        // Set the pre-shared key for the agent
        if (!agentConfig.DisablePreSharedKey)
            WebserverCore.Middlewares.PreSharedKeyFilter.PreSharedKey = PreSharedKey = RandomNumberGenerator.GetHexString(128);

        using var cts = new CancellationTokenSource();

        var target = new ControllerMultiLogTarget(new ConsoleLogDestination(), LogMessageType.Information, null);
        using (Log.StartScope(target))
        {
            if (OperatingSystem.IsWindows())
            {
                if (!WindowsEventLogSource.SourceExists(agentConfig.WindowsEventLog))
                {
                    Console.WriteLine("The Windows event log source does not exist, creating it");
                    try { WindowsEventLogSource.CreateEventSource(agentConfig.WindowsEventLog); }
                    catch (Exception ex) { Console.WriteLine("Failed to create the Windows event log source: {0}", ex.Message); }
                }

                if (WindowsEventLogSource.SourceExists(agentConfig.WindowsEventLog))
                    target.AddTarget(new WindowsEventLogSource(agentConfig.WindowsEventLog), LogMessageType.Information, null);
            }

            Log.WriteMessage(LogMessageType.Information, LogTag, "AgentStarting", "Starting agent");

            var settings = await Register(agentConfig.RegistrationUrl, agentConfig.AgentSettingsFile.FullName, cts.Token);

            var t = await Task.WhenAny(
                StartLocalServer(agentConfig, settings, args, cts.Token),
                KeepRemoteConnection.Start(
                    settings.ServerUrl,
                    settings.JWT,
                    settings.CertificateUrl,
                    settings.ServerCertificates,
                    cts.Token,
                    ReKey,
                    (m) => OnMessage(m, agentConfig)
                )
            );

            await t;

            return 0;
        }
    }

    private static async Task OnMessage(KeepRemoteConnection.CommandMessage message, CommandLineArguments agentConfig)
    {
        Log.WriteMessage(LogMessageType.Information, LogTag, "OnMessage", "Received message: {0}", message);

        var provider = FIXMEGlobal.Provider.GetRequiredService<IJWTTokenProvider>();
        var token = provider.CreateAccessToken("agent", provider.TemporaryFamilyId, TimeSpan.FromMinutes(2));
        using var httpClient = FIXMEGlobal.Provider.GetRequiredService<IHttpClientFactory>().CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        httpClient.BaseAddress = new Uri($"http://127.0.0.1:{agentConfig.WebservicePort}");

        if (!string.IsNullOrWhiteSpace(WebserverCore.Middlewares.PreSharedKeyFilter.PreSharedKey))
            httpClient.DefaultRequestHeaders.Add(WebserverCore.Middlewares.PreSharedKeyFilter.HeaderName, PreSharedKey);

        await message.Handle(httpClient);
    }

    private static Task ReKey(ClaimedClientData keydata)
    {
        Log.WriteMessage(LogMessageType.Information, LogTag, "ReKey", "Rekeying the settings");
        var settings = Settings.Load();
        if (!string.IsNullOrWhiteSpace(keydata.JWT) && settings.JWT != keydata.JWT)
            settings = settings with { JWT = keydata.JWT };
        if (keydata.ServerCertificates != null && keydata.ServerCertificates.Any())
            settings = settings with { ServerCertificates = keydata.ServerCertificates };

        if (!string.IsNullOrWhiteSpace(keydata.LocalEncryptionKey) && settings.SettingsEncryptionKey != keydata.LocalEncryptionKey)
        {
            // Log.WriteMessage(LogMessageType.Information, LogTag, "ReKey", "Changing the local settings encryption key");
            // TODO: Implement changing the database encryption key
            // FIXMEGlobal.Provider.GetRequiredService<Connection>().ChangeDbKey(keydata.LocalEncryptionKey);
            // settings = settings with { SettingsEncryptionKey = keydata.LocalEncryptionKey };
        }
        settings.Save();

        return Task.CompletedTask;
    }

    private static async Task RunServer(string[] args, CancellationToken cancellationToken)
    {
        cancellationToken.Register(() =>
        {
            Server.Program.ApplicationExitEvent.Set();
        });

        var lastRestart = DateTime.Now;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                lastRestart = DateTime.Now;
                Server.Program.Main(args);
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    var nextRestart = lastRestart.AddSeconds(30);
                    var waitTime = nextRestart - DateTime.Now;
                    if (waitTime.TotalSeconds < 0)
                        waitTime = TimeSpan.FromSeconds(0);
                    Log.WriteMessage(LogMessageType.Error, LogTag, "ServerCrash", ex, "Server crashed, restarting in {0} seconds", waitTime.TotalSeconds);
                    if (waitTime.TotalSeconds > 0)
                        await Task.Delay(waitTime);
                }
            }
        }
    }

    /// <summary>
    /// Registers the machine with the remote server
    /// </summary>
    /// <param name="registrationUrl">The URL to register the machine with</param>
    /// <param name="settingsFile">The file to save the settings to</param>
    /// <param name="cancellationToken">The cancellation token to use for the process</param>
    /// <returns>The settings for the machine</returns>
    private static async Task<Settings> Register(string registrationUrl, string settingsFile, CancellationToken cancellationToken)
    {
        var settings = Settings.Load(settingsFile);
        if (string.IsNullOrWhiteSpace(settings.JWT))
        {
            Log.WriteMessage(LogMessageType.Information, LogTag, "ClientNoJWT", "No JWT found in settings, starting in registration mode");
            using (var registration = new RegisterForRemote(registrationUrl, null, cancellationToken))
            {
                var registerClientData = await registration.Register();
                if (registerClientData.RegistrationData != null)
                    Log.WriteMessage(LogMessageType.Information, LogTag, "ClientRegistered", $"Machine registered, claim it by visiting: {registerClientData.RegistrationData.ClaimLink}");
                var claimedClientData = await registration.Claim();

                Log.WriteMessage(LogMessageType.Information, LogTag, "ClientClaimed", "Machine claimed, saving JWT");
                settings = settings with
                {
                    JWT = claimedClientData.JWT,
                    ServerUrl = claimedClientData.ServerUrl,
                    SettingsEncryptionKey = claimedClientData.LocalEncryptionKey,
                    ServerCertificates = claimedClientData.ServerCertificates
                };

                settings.Save();
            }
        }

        return settings;
    }

    /// <summary>
    /// Starts the local webserver in locked down mode, restarting on crashes
    /// </summary>
    /// <param name="agentConfig">The agent configuration</param>
    /// <param name="settings">The settings for the agent</param>
    /// <param name="args">The commandline arguments</param>
    /// <param name="cancellationToken">The cancellation token that stops the server</param>
    /// <returns>An awaitable task</returns>
    private static async Task StartLocalServer(CommandLineArguments agentConfig, Settings settings, string[] args, CancellationToken cancellationToken)
    {
        // TODO: Look into pipes for Kestrel to prevent network access

        // Lock down the instance, reset tokens and password
        args = args.Concat([
            $"--webservice-listen-interface={agentConfig.WebserviceListenInterface}",
            $"--webservice-password={agentConfig.WebservicePassword}",
            OperatingSystem.IsWindows() ? $"--windows-eventlog={agentConfig.WindowsEventLog}" : "",
            $"--webservice-port={agentConfig.WebservicePort}",
            $"--webservice-reset-jwt-config={agentConfig.WebserviceResetJwtConfig}",
            $"--webservice-allowed-hostnames={agentConfig.WebserviceAllowedHostnames}",
            $"--webservice-api-only={agentConfig.WebserviceApiOnly}",
            $"--webservice-disable-signin-tokens={agentConfig.WebserviceDisableSigninTokens}"
        ])
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .ToArray();

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY")))
            Environment.SetEnvironmentVariable("SETTINGS_ENCRYPTION_KEY", settings.SettingsEncryptionKey);

        // Start the server
        await RunServer(args, cancellationToken);
    }
}

