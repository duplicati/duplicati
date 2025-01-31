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

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.Reactive.Linq;
using System.Security.Cryptography;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Encryption;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Library.RemoteControl;
using Duplicati.Library.RestAPI;
using Duplicati.Library.Utility;
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
    /// <param name="AgentRegistrationUrl">The server URL to connect to</param>
    /// <param name="AgentSettingsFile">The file to use for the agent settings</param>
    /// <param name="AgentSettingsFilePassphrase">The passphrase for the agent settings file</param>
    /// <param name="WebserviceListenInterface">The interface to listen on for the webserver</param>
    /// <param name="WebservicePort">The port to listen on for the webserver</param>
    /// <param name="WebservicePassword">The password for the webserver, random if none supplied</param>
    /// <param name="SettingsEncryptionKey">The encryption key for the database settings</param>
    /// <param name="WindowsEventLog">The Windows event log to write to</param>
    /// <param name="DisableDbEncryption">Disable database encryption</param>
    /// <param name="WebserviceResetJwtConfig">Reset the JWT configuration</param>
    /// <param name="WebserviceAllowedHostnames">The allowed hostnames for the webserver</param>
    /// <param name="WebserviceApiOnly">Only allow API access to the webserver</param>
    /// <param name="WebserviceDisableSigninTokens">Disable signin tokens for the webserver</param>
    /// <param name="PingPongKeepalive">Enable ping-pong keepalive</param>
    /// <param name="DisablePreSharedKey">Disable the pre-shared key</param>
    /// <param name="KeepWebservicePassword">Keep the webserver password</param>
    /// <param name="SecretProvider">The secret provider to use</param>
    /// <param name="SecretProviderCache">The secret provider cache level</param>
    /// <param name="SecretProviderPattern">The secret provider pattern</param>
    private sealed record CommandLineArguments(
        string AgentRegistrationUrl,
        FileInfo AgentSettingsFile,
        string? AgentSettingsFilePassphrase,
        bool AgentRegisterOnly,
        string WebserviceListenInterface,
        string WebservicePort,
        string WebservicePassword,
        string? SettingsEncryptionKey,
        string WindowsEventLog,
        bool DisableDbEncryption,
        bool WebserviceResetJwtConfig,
        string WebserviceAllowedHostnames,
        bool WebserviceApiOnly,
        bool WebserviceDisableSigninTokens,
        bool PingPongKeepalive,
        bool DisablePreSharedKey,
        bool KeepWebservicePassword,
        string? SecretProvider,
        SecretProviderHelper.CachingLevel SecretProviderCache,
        string SecretProviderPattern
    );

    [STAThread]
    public static Task<int> Main(string[] args)
    {
        Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref args, Library.AutoUpdater.PackageHelper.NamedExecutable.Agent);

        var runcmd = new Command("run", "Runs the agent")
        {
            new Option<string>("--agent-registration-url", description: "The server URL to connect to", getDefaultValue: () => RegisterForRemote.DefaultRegisterationUrl),
            new Option<FileInfo>("--agent-settings-file", description: "The file to use for the agent settings", getDefaultValue: () => new FileInfo(Settings.DefaultSettingsFile)),
            new Option<string?>("--agent-settings-file-passphrase", description: "The passphrase for the agent settings file", getDefaultValue: () => null),
            new Option<bool>("--agent-register-only", description: "Only register the agent, then exit", getDefaultValue: () => false),
            new Option<string>("--webservice-listen-interface", description: "The interface to listen on for the webserver", getDefaultValue: () => "loopback"),
            new Option<string>("--webservice-port", description: "The port to listen on for the webserver", getDefaultValue: () => "8210"),
            new Option<string?>("--webservice-password", description: "The password for the webserver, not set, or set to \"random\", a random value is used.", getDefaultValue: () => null),
            new Option<string?>("--settings-encryption-key", description: "The encryption key for the database settings", getDefaultValue: () => null),
            new Option<string>("--windows-eventlog", description: "The Windows event log to use as source.", getDefaultValue: () => ""),
            new Option<bool>("--disable-db-encryption", description: "Disable database encryption", getDefaultValue: () => false),
            new Option<bool>("--webservice-reset-jwt-config", description: "Reset the JWT configuration", getDefaultValue: () => true),
            new Option<string>("--webservice-allowed-hostnames", description: "The allowed hostnames for the webserver", getDefaultValue: () => "127.0.0.1"),
            new Option<bool>("--webservice-api-only", description: "Only allow API access to the webserver", getDefaultValue: () => true),
            new Option<bool>("--webservice-disable-signin-tokens", description: "Disable signin tokens for the webserver", getDefaultValue: () => true),
            new Option<bool>("--ping-pong-keepalive", description: "Enable ping-pong keepalive", getDefaultValue: () => false),
            new Option<bool>("--disable-pre-shared-key", description: "Disable the pre-shared key that prevents outside access to the webserver", getDefaultValue: () => false),
            new Option<bool>("--keep-webservice-password", description: "Disables the random password assigned to the webserver on startup if no password is provided", getDefaultValue: () => false),
            new Option<string?>("--secret-provider", description: "The secret provider to use", getDefaultValue: () => null),
            new Option<SecretProviderHelper.CachingLevel>("--secret-provider-cache", description: "The secret provider cache level", getDefaultValue: () => SecretProviderHelper.CachingLevel.None),
            new Option<string>("--secret-provider-pattern", description: "The secret provider pattern", getDefaultValue: () => SecretProviderHelper.DEFAULT_PATTERN),
            new Option<bool>($"--{DataFolderManager.PORTABLE_MODE_OPTION}", description: "Use portable mode for locating the database and storing configuration", getDefaultValue: () => DataFolderManager.PORTABLE_MODE),
            new Option<DirectoryInfo?>($"--{DataFolderManager.SERVER_DATAFOLDER_OPTION}", description: "The datafolder to use for locating the database and storing configuration", getDefaultValue: () => new DirectoryInfo(DataFolderManager.DATAFOLDER)),
        };
        runcmd.Handler = CommandHandler.Create<CommandLineArguments>(RunAgent);

        var clearCommand = new Command("clear", "Clears the agent settings")
        {
            new Option<FileInfo>("--agent-settings-file", description: "The file to use for the agent settings", getDefaultValue: () => new FileInfo(Settings.DefaultSettingsFile)),
        };
        clearCommand.Handler = CommandHandler.Create<FileInfo>(ClearAgentSettings);

        var registerCommand = new Command("register", "Registers the agent and exits")
        {
            new Argument<string>("agent-registration-url", description: "The server URL to connect to", getDefaultValue: () => RegisterForRemote.DefaultRegisterationUrl) { Arity = ArgumentArity.ZeroOrOne },
            new Option<FileInfo>("--agent-settings-file", description: "The file to use for the agent settings", getDefaultValue: () => new FileInfo(Settings.DefaultSettingsFile)),
            new Option<string?>("--agent-settings-file-passphrase", description: "The passphrase for the agent settings file", getDefaultValue: () => null),
            new Option<string?>("--secret-provider", description: "The secret provider to use", getDefaultValue: () => null),
            new Option<string>("--secret-provider-pattern", description: "The secret provider pattern", getDefaultValue: () => SecretProviderHelper.DEFAULT_PATTERN),
        };
        registerCommand.Handler = CommandHandler.Create<string, FileInfo, string?, string?, string>(RegisterAgent);

        var rootcmd = new RootCommand("Duplicati Agent")
        {
            runcmd,
            clearCommand,
            registerCommand
        };
        // rootcmd.Handler = CommandHandler.Create<CommandLineArguments>(RunAgent);

        return new CommandLineBuilder(rootcmd)
            .UseDefaults()
            .UseExceptionHandler((ex, context) =>
            {
                if (ex is UserInformationException userInformationException)
                {
                    Console.WriteLine("ErrorID: {0}", userInformationException.HelpID);
                    Console.WriteLine("Message: {0}", userInformationException.Message);
                    context.ExitCode = 2;
                }
                else
                {
                    Console.WriteLine("Exception: " + ex);
                    context.ExitCode = 1;
                }
            })
            .UseAdditionalHelpAliases()
            .Build()
            .InvokeAsync(args);
    }

    /// <summary>
    /// Clears any local agent settings
    /// </summary>
    /// <param name="agentSettingsFile">The file to clear</param>
    /// <returns>The exit code</returns>
    private static Task<int> ClearAgentSettings(FileInfo agentSettingsFile)
    {
        if (File.Exists(agentSettingsFile.FullName))
            File.Delete(agentSettingsFile.FullName);
        return Task.FromResult(0);
    }

    /// <summary>
    /// Registers the agent with the remote server
    /// </summary>
    /// <param name="agentRegistrationUrl">The URL to register the agent with</param>
    /// <param name="agentSettingsFile">The file to save the settings to</param>
    /// <param name="agentSettingsFilePassphrase">The passphrase for the settings file</param>
    /// <param name="secretProvider">The secret provider to use</param>
    /// <param name="secretProviderPattern">The secret provider pattern</param>
    /// <returns></returns>
    private static Task<int> RegisterAgent(string agentRegistrationUrl, FileInfo agentSettingsFile, string? agentSettingsFilePassphrase, string? secretProvider, string secretProviderPattern)
        => RunAgent(new CommandLineArguments(
            AgentRegistrationUrl: string.IsNullOrWhiteSpace(agentRegistrationUrl)
                ? RegisterForRemote.DefaultRegisterationUrl
                : agentRegistrationUrl,
            AgentSettingsFile: agentSettingsFile,
            AgentSettingsFilePassphrase: agentSettingsFilePassphrase,
            AgentRegisterOnly: true,
            WebserviceListenInterface: "loopback",
            WebservicePort: "8210",
            WebservicePassword: null!,
            SettingsEncryptionKey: null,
            WindowsEventLog: "",
            DisableDbEncryption: false,
            WebserviceResetJwtConfig: false,
            WebserviceAllowedHostnames: "",
            WebserviceApiOnly: true,
            WebserviceDisableSigninTokens: true,
            PingPongKeepalive: false,
            DisablePreSharedKey: false,
            KeepWebservicePassword: false,
            SecretProvider: secretProvider,
            SecretProviderCache: SecretProviderHelper.CachingLevel.None,
            SecretProviderPattern: secretProviderPattern
        ));

    /// <summary>
    /// Runs the agent
    /// </summary>
    /// <param name="agentConfig">The configuration from the commandline</param>
    /// <returns>The exit code</returns>
    private static async Task<int> RunAgent(CommandLineArguments agentConfig)
    {
        // Read the environment variable for the encryption key
        if (string.IsNullOrWhiteSpace(agentConfig.SettingsEncryptionKey))
            agentConfig = agentConfig with { SettingsEncryptionKey = Environment.GetEnvironmentVariable(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME) };

        // Apply the secret provider, if present
        if (!string.IsNullOrWhiteSpace(agentConfig.SecretProvider))
        {
            // We can set any string properties on the agent configuration
            var agentProps = agentConfig.GetType().GetProperties()
                .Where(x => x.CanRead && x.CanWrite)
                .Where(x => !x.Name.StartsWith(nameof(CommandLineArguments.SecretProvider)))
                .Where(x => x.PropertyType == typeof(string) || Nullable.GetUnderlyingType(x.PropertyType) == typeof(string))
                .ToDictionary(x => x.Name, x => x);

            // Grab potential options from the agent configuration
            var options = agentProps.Select(x => (x.Key, Value: x.Value.GetValue(agentConfig)?.ToString()))
                .Where(x => x.Value != null)
                .ToDictionary(x => x.Key, x => x.Value);

            // Patch with the secret provider options
            options["secret-provider"] = agentConfig.SecretProvider;
            options["secret-provider-cache"] = agentConfig.SecretProviderCache.ToString();
            options["secret-provider-pattern"] = agentConfig.SecretProviderPattern;

            FIXMEGlobal.SecretProvider = await SecretProviderHelper.ApplySecretProviderAsync([], [], options, TempFolder.SystemTempPath, null, CancellationToken.None);

            // Apply the secret provider to the agent configuration
            foreach (var prop in agentProps)
                if (options.TryGetValue(prop.Key, out string? value))
                    prop.Value.SetValue(agentConfig, value);
        }

        if (agentConfig.KeepWebservicePassword)
        {
            // Check for conflicting options, require no password
            if (!string.IsNullOrWhiteSpace(agentConfig.WebservicePassword))
                throw new UserInformationException("Cannot use --keep-webservice-password with a provided password", "KeepWebservicePasswordWithPassword");
        }
        else
        {
            // Prevent access to the webserver interface from anything but the agent
            if (string.Equals("random", agentConfig.WebservicePassword, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(agentConfig.WebservicePassword))
                agentConfig = agentConfig with { WebservicePassword = System.Security.Cryptography.RandomNumberGenerator.GetHexString(128) };
        }

        // Set the pre-shared key for the agent
        if (!agentConfig.DisablePreSharedKey)
            WebserverCore.Middlewares.PreSharedKeyFilter.PreSharedKey = PreSharedKey = RandomNumberGenerator.GetHexString(128);

        using var cts = new CancellationTokenSource();

        var target = new ControllerMultiLogTarget(new ConsoleLogDestination(), LogMessageType.Information, null);
        using (Log.StartScope(target))
        {
            if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(agentConfig.WindowsEventLog))
            {
                if (!WindowsEventLogSource.SourceExists(agentConfig.WindowsEventLog))
                {
                    Console.WriteLine($"The Windows event log {agentConfig.WindowsEventLog} does not exist, creating it");
                    try { WindowsEventLogSource.CreateEventSource(agentConfig.WindowsEventLog); }
                    catch (Exception ex) { Console.WriteLine($"Failed to create the Windows event log {agentConfig.WindowsEventLog}: {ex.Message}"); }
                }

                if (WindowsEventLogSource.SourceExists(agentConfig.WindowsEventLog))
                    target.AddTarget(new WindowsEventLogSource(agentConfig.WindowsEventLog), LogMessageType.Information, null);
            }

            Log.WriteMessage(LogMessageType.Information, LogTag, "AgentStarting", "Starting agent");

            var settings = await Register(agentConfig.AgentRegistrationUrl, agentConfig.AgentSettingsFile.FullName, agentConfig.AgentSettingsFilePassphrase, cts.Token);

            if (agentConfig.AgentRegisterOnly)
            {
                Log.WriteMessage(LogMessageType.Information, LogTag, "ClientRegistered", "Client registered, exiting");
                return 0;
            }

            var t = await Task.WhenAny(
                Task.Run(() => StartLocalServer(agentConfig, settings, cts.Token)),
                KeepRemoteConnection.Start(
                    settings.ServerUrl,
                    settings.JWT,
                    settings.CertificateUrl,
                    settings.ServerCertificates,
                    cts.Token,
                    c => ReKey(agentConfig, c),
                    (m) => OnMessage(m, agentConfig)
                )
            );

            await t;

            return 0;
        }
    }

    private static async Task OnMessage(KeepRemoteConnection.CommandMessage message, CommandLineArguments agentConfig)
    {
        Log.WriteMessage(LogMessageType.Verbose, LogTag, "OnMessage", "Received message: {0}", message);

        var provider = FIXMEGlobal.Provider.GetRequiredService<IJWTTokenProvider>();
        var token = provider.CreateAccessToken("agent", provider.TemporaryFamilyId, TimeSpan.FromMinutes(2));
        using var httpClient = FIXMEGlobal.Provider.GetRequiredService<IHttpClientFactory>().CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        httpClient.BaseAddress = new System.Uri($"http://127.0.0.1:{agentConfig.WebservicePort}");

        if (!string.IsNullOrWhiteSpace(WebserverCore.Middlewares.PreSharedKeyFilter.PreSharedKey))
            httpClient.DefaultRequestHeaders.Add(WebserverCore.Middlewares.PreSharedKeyFilter.HeaderName, PreSharedKey);

        await message.Handle(httpClient);
    }

    private static Task ReKey(CommandLineArguments agentConfig, ClaimedClientData keydata)
    {
        Log.WriteMessage(LogMessageType.Verbose, LogTag, "ReKey", "Rekeying the settings");
        var settings = Settings.Load(agentConfig.AgentSettingsFile.FullName, agentConfig.AgentSettingsFilePassphrase);
        if (!string.IsNullOrWhiteSpace(keydata.JWT) && settings.JWT != keydata.JWT)
            settings = settings with { JWT = keydata.JWT };
        if (keydata.ServerCertificates != null && keydata.ServerCertificates.Any())
            settings = settings with { ServerCertificates = keydata.ServerCertificates };

        // Only allow re-keying if the settings encryption key is not set
        if (!FIXMEGlobal.SettingsEncryptionKeyProvidedExternally)
        {
            if (!string.IsNullOrWhiteSpace(keydata.LocalEncryptionKey) && settings.SettingsEncryptionKey != keydata.LocalEncryptionKey)
            {
                // Log.WriteMessage(LogMessageType.Verbose, LogTag, "ReKey", "Changing the local settings encryption key");
                // TODO: Implement changing the database encryption key
                // FIXMEGlobal.Provider.GetRequiredService<Connection>().ChangeDbKey(keydata.LocalEncryptionKey);
                // settings = settings with { SettingsEncryptionKey = keydata.LocalEncryptionKey };
            }
        }
        settings.Save();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs the server, restarting on crashes
    /// </summary>
    /// <param name="args">The commandline arguments passed to the server</param>
    /// <param name="cancellationToken">The cancellation token to use for the process</param>
    /// <returns>An awaitable task</returns>
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
    /// <param name="settingsPasshrase">The passphrase for the settings file</param>
    /// <param name="cancellationToken">The cancellation token to use for the process</param>
    /// <returns>The settings for the machine</returns>
    private static async Task<Settings> Register(string registrationUrl, string settingsFile, string? settingsPasshrase, CancellationToken cancellationToken)
    {
        var settings = Settings.Load(settingsFile, settingsPasshrase);
        if (string.IsNullOrWhiteSpace(settings.JWT))
        {
            Log.WriteMessage(LogMessageType.Information, LogTag, "ClientNoJWT", "No JWT found in settings, starting in registration mode");
            using (var registration = new RegisterForRemote(registrationUrl, null, cancellationToken))
            {
                var registerClientData = await registration.Register();
                if (registerClientData.RegistrationData != null)
                {
                    Log.WriteMessage(LogMessageType.Information, LogTag, "ClientRegistered", $"Machine registered, claim it by visiting: {registerClientData.RegistrationData.ClaimLink}");

                    try
                    {
                        Library.Utility.UrlUtility.OpenURL(registerClientData.RegistrationData.ClaimLink);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteMessage(LogMessageType.Warning, LogTag, "ClientClaimLink", ex, "Failed to open the claim link in system browser");
                    }
                }
                var claimedClientData = await registration.Claim();

                Log.WriteMessage(LogMessageType.Information, LogTag, "ClientClaimed", "Machine claimed, saving JWT");
                settings = settings with
                {
                    JWT = claimedClientData.JWT,
                    ServerUrl = claimedClientData.ServerUrl,
                    SettingsEncryptionKey = claimedClientData.LocalEncryptionKey,
                    ServerCertificates = claimedClientData.ServerCertificates,
                    CertificateUrl = claimedClientData.CertificateUrl
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
    /// <param name="cancellationToken">The cancellation token that stops the server</param>
    /// <returns>An awaitable task</returns>
    private static async Task StartLocalServer(CommandLineArguments agentConfig, Settings settings, CancellationToken cancellationToken)
    {
        // TODO: Look into pipes for Kestrel to prevent network access

        string? settingsEncryptionKey = null;

        // Prefer the local key for the settings encryption
        if (!string.IsNullOrWhiteSpace(agentConfig.SettingsEncryptionKey))
            settingsEncryptionKey = agentConfig.SettingsEncryptionKey;
        // Otherwise use server provided key
        else if (!string.IsNullOrWhiteSpace(settings.SettingsEncryptionKey))
            settingsEncryptionKey = settings.SettingsEncryptionKey;

        // Helper method to support empty arguments
        static string EncodeOption(string option, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return $"{option}={value}";
        }

        // Lock down the instance, reset tokens and password,
        // and forward relevant settings from agent to the webserver
        var args = new[] {
            EncodeOption("--webservice-listen-interface", agentConfig.WebserviceListenInterface),
            EncodeOption("--webservice-password", agentConfig.WebservicePassword),
            EncodeOption("--webservice-port", agentConfig.WebservicePort),
            EncodeOption("--webservice-reset-jwt-config", agentConfig.WebserviceResetJwtConfig.ToString()),
            EncodeOption("--webservice-allowed-hostnames", agentConfig.WebserviceAllowedHostnames),
            EncodeOption("--webservice-api-only", agentConfig.WebserviceApiOnly.ToString()),
            EncodeOption("--webservice-disable-signin-tokens", agentConfig.WebserviceDisableSigninTokens.ToString()),
            EncodeOption("--disable-db-encryption", agentConfig.DisableDbEncryption.ToString()),
            EncodeOption("--settings-encryption-key", settingsEncryptionKey),
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        // Set the global origin
        FIXMEGlobal.Origin = "Agent";

        // Start the server
        await RunServer(args, cancellationToken);
    }
}

