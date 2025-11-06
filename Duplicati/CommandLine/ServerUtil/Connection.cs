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

using System.Net.Http.Json;
using System.Net.Security;
using System.Text.Json;
using Duplicati.Library.AutoUpdater;
using Duplicati.WebserverCore.Middlewares;
using Duplicati.WebserverCore.Services;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Implementation of actions performed on the server.
/// </summary>
public class Connection
{
    /// <summary>
    /// The reported backup data
    /// </summary>
    /// <param name="ID">The ID of the backup</param>
    /// <param name="Name">The name of the backup</param>
    /// <param name="Description">The description of the backup</param>
    /// <param name="Metadata">The metadata of the backup</param>
    public sealed record BackupEntry(
        string ID,
        string Name,
        string Description,
        Dictionary<string, string>? Metadata,
        BackupSchedule? Schedule
    );

    /// <summary>
    /// The schedule attached to a backup
    /// </summary>
    /// <param name="Time">The time when the backup runs</param>
    /// <param name="Repeat">The </param>
    /// <param name="LastRun"></param>
    /// <param name="Rule"></param>
    /// <param name="AllowedDays"></param>
    public sealed record BackupSchedule(
        string? Time,
        string? Repeat,
        string? LastRun,
        string? Rule,
        string[]? AllowedDays
    );

    /// <summary>
    /// The response backup data returned from the server
    /// </summary>
    /// <param name="Backup">The backup details</param>
    /// <param name="Schedule">The schedule of the backup</param>
    private sealed record ResponseBackupEntry(ResponseBackupEntry.ResponseBackupDetailsEntry Backup, BackupSchedule? Schedule)
    {
        /// <summary>
        /// The response backup details entry
        /// </summary>
        /// <param name="ID">The ID of the backup</param>
        /// <param name="Name">The name of the backup</param>
        /// <param name="Description">The description of the backup</param>
        /// <param name="DBPath">The path to the local database</param>
        /// <param name="Metadata">The metadata of the backup</param>
        public sealed record ResponseBackupDetailsEntry(
            string ID,
            string Name,
            string? Description,
            string? DBPath,
            Dictionary<string, string>? Metadata
        );

        /// <summary>
        /// Converts the response backup entry to a backup entry
        /// </summary>
        /// <returns>The backup entry</returns>
        public BackupEntry ToBackupEntry()
            => new BackupEntry(Backup.ID, Backup.Name, Backup.Description ?? "", Backup.Metadata, Schedule);
    }

    /// <summary>
    /// The task entry
    /// </summary>
    /// <param name="TaskID">The ID of the task</param>
    /// <param name="BackupID">The ID of the backup</param>
    /// <param name="Operation">The operation of the task</param>
    public sealed record TaskEntry(
        long TaskID,
        string BackupID,
        string Operation
    );

    /// <summary>
    /// The server state
    /// </summary>
    /// <param name="ActiveTask">The active task, if any</param>
    /// <param name="ProgramState">The state of the server</param>
    /// <param name="SchedulerQueueIds">The IDs of the tasks in the scheduler queue</param>
    public sealed record ServerState(
        Tuple<long, string>? ActiveTask,
        string ProgramState,
        IList<Tuple<long, string>> SchedulerQueueIds
    );

    /// <summary>
    /// The stop level
    /// </summary>
    public enum StopLevel
    {
        /// <summary>
        /// Stop after the current file
        /// </summary>
        AfterCurrentFile,
        /// <summary>
        /// Stop now
        /// </summary>
        StopNow,
        /// <summary>
        /// Stop immediately
        /// </summary>
        Abort
    }

    /// <summary>
    /// The HTTP client used to connect to the server
    /// </summary>
    private readonly HttpClient client;

    /// <summary>
    /// Initializes a new instance of the <see cref="Connection"/> class
    /// </summary>
    private Connection(HttpClient client)
    {
        this.client = client;
    }

    /// <summary>
    /// Connects to the server
    /// </summary>
    /// <param name="settings">The settings to use for the connection</param>
    /// <param name="obtainRefreshToken">Whether to obtain a refresh token</param>
    /// <param name="console">Console messages interceptor</param>
    /// <returns>The connection</returns>
    public static async Task<Connection> Connect(Settings settings, bool obtainRefreshToken = false, OutputInterceptor? console = null)
    {
        if (console != null)
            console.AppendConsoleMessage($"Connecting to {settings.HostUrl}...");
        else
            Console.WriteLine($"Connecting to {settings.HostUrl}...");

        var trustedCertificateHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(settings.AcceptedHostCertificate))
            trustedCertificateHashes.UnionWith(settings.AcceptedHostCertificate.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // Configure the client for requests
        var client = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = settings.Insecure || trustedCertificateHashes.Contains("*")
               ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
               : ((message, cert, chain, sslPolicyErrors) =>
                {
                    if (sslPolicyErrors == SslPolicyErrors.None)
                        return true;
                    if (cert == null)
                        return false;
                    return trustedCertificateHashes.Contains(cert.GetCertHashString());
                })
        })
        {
            BaseAddress = new Uri(settings.HostUrl + "api/v1/")
        };

        // If we already have a refresh token, try that first
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.RefreshToken))
            {
                var (accessToken, refreshToken, refreshNonce) = await LoginWithRefreshToken(client, settings.RefreshToken, settings.RefreshNonce);
                if (string.IsNullOrWhiteSpace(accessToken))
                    throw new InvalidOperationException("Failed to get access token");
                if (string.IsNullOrWhiteSpace(refreshToken))
                    throw new InvalidOperationException("Failed to get refresh token");

                (settings with { RefreshToken = refreshToken, RefreshNonce = refreshNonce }).Save(console);
                return CreateConnectionWithClient(client, accessToken);
            }
        }
        catch (Exception ex)
        {
            if (console != null)
                console.AppendExceptionMessage($"Failed to use refresh token: {ex.Message}");
            else
                Console.WriteLine($"Failed to use refresh token: {ex.Message}");
        }

        // If we can read the server database, try to create a signin token
        // But don't try to look at the database if the password is already set.
        if (string.IsNullOrWhiteSpace(settings.Password))
        {
            try
            {
                var opts = new Dictionary<string, string>();
                if (settings.Key != null)
                    opts["settings-encryption-key"] = settings.Key.Key;
                if (File.Exists(Path.Combine(DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly), DataFolderManager.SERVER_DATABASE_FILENAME)))
                {
                    string? cfg = null;
                    using (var connection = Server.Program.GetDatabaseConnection(new ApplicationSettings(), opts, true, false))
                    {
                        cfg = connection.ApplicationSettings.JWTConfig;
                        if (settings.HostUrl.Scheme == "https" && connection.ApplicationSettings.ServerSSLCertificate != null && trustedCertificateHashes.Count == 0)
                        {
                            var selfSignedCertHash = connection.ApplicationSettings.ServerSSLCertificate?.FirstOrDefault(x => x.HasPrivateKey)?.GetCertHashString();
                            if (!string.IsNullOrWhiteSpace(selfSignedCertHash))
                                trustedCertificateHashes.Add(selfSignedCertHash);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(cfg))
                    {
                        var signinjwt = new JWTTokenProvider(
                            JsonSerializer.Deserialize<JWTConfig>(cfg)
                                ?? throw new InvalidOperationException("Failed to deserialize JWTConfig")
                        ).CreateSigninToken("server-cli");

                        var responseTask = client.PostAsync("auth/signin", JsonContent.Create(new { SigninToken = signinjwt, RememberMe = obtainRefreshToken }));
                        var (accessToken, refreshToken, refreshNonce) = await ParseAuthResponse(responseTask);
                        if (string.IsNullOrWhiteSpace(accessToken))
                            throw new InvalidOperationException("Failed to get access token");

                        if (!string.IsNullOrWhiteSpace(refreshToken) && obtainRefreshToken)
                            (settings with { RefreshToken = refreshToken, RefreshNonce = refreshNonce }).Save(console);

                        return CreateConnectionWithClient(client, accessToken);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly)))
                {
                    if (console != null)
                        console.AppendConsoleMessage($"No database found in {DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly)}");
                    else
                        Console.WriteLine($"No database found in {DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly)}");
                }
            }
            catch (Exception ex)
            {
                if (console != null)
                    console.AppendConsoleMessage($"Failed to obtain a signin token: {ex.Message}");
                else
                    Console.WriteLine($"Failed to obtain a signin token: {ex.Message}");
            }
        }

        // Otherwise, we need a password to log in
        try
        {
            // Try obtaining the password from the user
            if (string.IsNullOrWhiteSpace(settings.Password))
                settings = settings with { Password = HelperMethods.ReadPasswordFromConsole("Enter server password: ") };

            if (string.IsNullOrWhiteSpace(settings.Password))
                throw new UserReportedException("Password is required");

            var (accessToken, refreshToken, refreshNonce) = await LoginWithPassword(client, settings.Password, obtainRefreshToken);
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("Failed to get access token");

            if (!string.IsNullOrWhiteSpace(refreshToken) && obtainRefreshToken)
                (settings with { RefreshToken = refreshToken, RefreshNonce = refreshNonce }).Save(console);

            return CreateConnectionWithClient(client, accessToken);
        }
        catch (Exception ex)
        {
            client.Dispose();
            throw new UserReportedException($"Failed to connect to server: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates the connection and adds the authorization header
    /// </summary>
    /// <param name="client">The HTTP client</param>
    /// <param name="accessToken">The access token</param>
    /// <returns>The connection</returns>
    private static Connection CreateConnectionWithClient(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        return new Connection(client);
    }

    /// <summary>
    /// Logs in with a password
    /// </summary>
    /// <param name="client">The HTTP client</param>
    /// <param name="password">The password to use</param>
    /// <param name="obtainRefreshToken">Whether to obtain a refresh token</param>
    /// <returns>The access and refresh tokens</returns>
    private static Task<(string AccessToken, string? RefreshToken, string? RefreshNonce)> LoginWithPassword(HttpClient client, string password, bool obtainRefreshToken)
        => ParseAuthResponse(
            client.PostAsync("auth/login", JsonContent.Create(new { Password = password, RememberMe = obtainRefreshToken }))
        );

    /// <summary>
    /// Logs in with a refresh token
    /// </summary>
    /// <param name="client">The HTTP client</param>
    /// <param name="refreshToken">The refresh token to use</param>
    /// <returns>The access token, refresh tokens, and nonce</returns>
    private static Task<(string AccessToken, string? RefreshToken, string? RefreshNonce)> LoginWithRefreshToken(HttpClient client, string refreshToken, string? nonce)
    {
        return ParseAuthResponse(client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "auth/refresh")
        {
            Headers = { { "Cookie", $"RefreshToken_{client.BaseAddress!.Port}={refreshToken}" } },
            Content = string.IsNullOrWhiteSpace(nonce) ? null : JsonContent.Create(new { Nonce = nonce })
        }));
    }


    /// <summary>
    /// Parses the authentication response
    /// </summary>
    /// <param name="response">The response to parse</param>
    /// <returns>The access and refresh tokens</returns>
    private static async Task<(string AccessToken, string? RefreshToken, string? RefreshNonce)> ParseAuthResponse(Task<HttpResponseMessage> responseTask)
    {
        var response = await responseTask;
        await EnsureSuccessStatusCodeWithParsing(response);
        var json = JsonSerializer.Deserialize<Dictionary<string, string>>(response.Content.ReadAsStringAsync().Result)
            ?? throw new InvalidOperationException("Failed to parse response");

        if (!json.TryGetValue("AccessToken", out var accessToken))
            throw new InvalidOperationException("Failed to get access token");

        json.TryGetValue("RefreshNonce", out var refreshNonce);

        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        var refreshToken = cookies?.SelectMany(c => c.Split(';')).FirstOrDefault(c => c.StartsWith("RefreshToken_"))?.Split('=', 2)[1];

        return (accessToken, refreshToken, refreshNonce);
    }

    /// <summary>
    /// Pauses the server
    /// </summary>
    /// <param name="duration">The duration to pause for</param>
    /// <returns>The task</returns>
    public async Task Pause(string? duration)
    {
        var query = string.IsNullOrWhiteSpace(duration) ? "" : $"?duration={Uri.EscapeDataString(duration)}";
        var response = await client.PostAsync($"serverstate/pause{query}", null);
        await EnsureSuccessStatusCodeWithParsing(response);
    }

    /// <summary>
    /// Resumes the server
    /// </summary>
    /// <returns>The task</returns>
    public async Task Resume()
    {
        var response = await client.PostAsync("serverstate/resume", null);
        await EnsureSuccessStatusCodeWithParsing(response);
    }

    /// <summary>
    /// Lists the backups configured on the server
    /// </summary>
    /// <returns>The backups</returns>
    public async Task<IEnumerable<BackupEntry>> ListBackups()
    {
        var response = await client.GetAsync("backups");
        await EnsureSuccessStatusCodeWithParsing(response);

        return (JsonSerializer.Deserialize<IEnumerable<ResponseBackupEntry>>(await response.Content.ReadAsStringAsync())
            ?? throw new UserReportedException("Failed to parse response"))
            .Select(x => x.ToBackupEntry())
            .ToArray();
    }

    /// <summary>
    /// Gets a backup by ID
    /// </summary>
    /// <param name="backupId">The ID of the backup</param>
    /// <returns>The backup</returns>
    public async Task<BackupEntry> GetBackup(string backupId)
    {
        var response = await client.GetAsync($"backup/{Uri.EscapeDataString(backupId)}");
        await EnsureSuccessStatusCodeWithParsing(response);

        return (JsonSerializer.Deserialize<ResponseBackupEntry>(await response.Content.ReadAsStringAsync())
            ?? throw new UserReportedException("Failed to parse response"))
            .ToBackupEntry();
    }

    /// <summary>
    /// Runs a backup
    /// </summary>
    /// <param name="backupId">The ID of the backup</param>
    /// <param name="skipQueue">Whether to skip the queue</param>
    /// <returns>The task</returns>
    public async Task RunBackup(string backupId, bool skipQueue)
    {
        var response = await client.PostAsync($"backup/{Uri.EscapeDataString(backupId)}/run?skipqueue={skipQueue}", null);
        await EnsureSuccessStatusCodeWithParsing(response);
    }

    /// <summary>
    /// Gets the server state
    /// </summary>
    /// <returns>The server state</returns>
    public async Task<ServerState> GetServerState()
    {
        var response = await client.GetAsync("serverstate");
        await EnsureSuccessStatusCodeWithParsing(response);
        return await response.Content.ReadFromJsonAsync<ServerState>()
            ?? throw new InvalidDataException("Failed to parse server response");
    }

    /// <summary>
    /// Gets a backup's most recent log status by backup ID
    /// </summary>
    /// <param name="backupId">The ID of the backup</param>
    /// <returns>The status of the most recent backup log</returns>
    public async Task<string> GetBackupStatus(string backupId)
    {
        var response = await client.GetAsync($"backup/{Uri.EscapeDataString(backupId)}/log");
        await EnsureSuccessStatusCodeWithParsing(response);

        var parsedLogs = await response.Content.ReadFromJsonAsync<List<Dictionary<string, object>>>()
            ?? throw new UserReportedException("Failed to parse response");

        if (parsedLogs.Count < 1)
        {
            return "Failed to run";
        }
        var lastLogResultString = ((JsonElement)parsedLogs[0]["Message"]).GetString();
        var lastLogResult = JsonSerializer.Deserialize<JsonElement>(lastLogResultString);
        return lastLogResult.GetProperty("ParsedResult").ToString();
    }

    /// <summary>
    /// Runs a backup
    /// </summary>
    /// <param name="backupId">The ID of the backup</param>
    /// <returns>The task</returns>
    public async Task WaitForBackup(string backupId, TimeSpan delay, Action<string> statusMessage)
    {
        var state = await GetServerState();

        if (!state.SchedulerQueueIds.Any(x => x.Item2 == backupId) && state.ActiveTask?.Item2 != backupId)
            throw new UserReportedException("Backup is not queued or running");

        var hasStarted = state.ActiveTask?.Item2 == backupId;

        while (true)
        {
            await Task.Delay(delay);
            state = await GetServerState();

            if (state.ActiveTask?.Item2 == backupId)
            {
                statusMessage("Backup is running ...");
                hasStarted = true;
                continue;
            }

            if (!hasStarted && state.SchedulerQueueIds.Any(x => x.Item2 == backupId))
            {
                statusMessage("Backup is queued ...");
                continue;
            }

            break;
        }
    }

    /// <summary>
    /// Lists the active tasks
    /// </summary>
    /// <returns>The tasks</returns>
    public async Task<IEnumerable<TaskEntry>> ListTasks()
    {
        var response = await client.GetAsync("tasks");
        await EnsureSuccessStatusCodeWithParsing(response);

        return JsonSerializer.Deserialize<IEnumerable<TaskEntry>>(await response.Content.ReadAsStringAsync())
            ?? throw new InvalidOperationException("Failed to parse response");
    }

    /// <summary>
    /// Stops a task
    /// </summary>
    /// <param name="taskId">The ID of the task</param>
    /// <param name="level">The level to stop at</param>
    /// <returns>The task</returns>
    public async Task StopTask(string taskId, StopLevel level = StopLevel.AfterCurrentFile)
    {
        var levelString = level switch
        {
            StopLevel.AfterCurrentFile => "stopaftercurrentfile",
            StopLevel.StopNow => "stopnow",
            StopLevel.Abort => "abort",
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
        var response = await client.PostAsync($"task/{Uri.EscapeDataString(taskId)}/{levelString}", null);
        await EnsureSuccessStatusCodeWithParsing(response);
    }

    /// <summary>
    /// Logs out of the server
    /// </summary>
    /// <param name="settings">The settings to use</param>
    /// <param name="output"></param>
    /// <returns>The task</returns>
    public async Task Logout(Settings settings, OutputInterceptor output)
    {
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "auth/refresh/logout")
        {
            Headers = { { "Cookie", $"RefreshToken_{client.BaseAddress!.Port}={settings.RefreshToken}" } }
        });
        await EnsureSuccessStatusCodeWithParsing(response);
        (settings with { RefreshToken = null }).Save(output);
    }

    /// <summary>
    /// Changes the server password
    /// </summary>
    /// <param name="newPassword">The new password to use</param>
    /// <returns>The task</returns>
    public async Task ChangePassword(string newPassword)
    {
        var response = await client.PutAsync("serversetting/server-passphrase", JsonContent.Create(newPassword));
        await EnsureSuccessStatusCodeWithParsing(response);
    }

    /// <summary>
    /// Imports a backup
    /// </summary>
    /// <param name="file">The file to import</param>
    /// <param name="password">The password to use</param>
    /// <param name="importMetadata">Whether to import metadata</param>
    /// <returns>The backup</returns>
    public async Task<BackupEntry> ImportBackup(string file, string? password, bool importMetadata)
    {
        var payload = JsonContent.Create(new
        {
            config = Convert.ToBase64String(await File.ReadAllBytesAsync(file)),
            import_metadata = importMetadata,
            passphrase = password,
            direct = true
        });
        var response = await client.PostAsync("backups/import", payload);
        await EnsureSuccessStatusCodeWithParsing(response);

        var json = JsonSerializer.Deserialize<Dictionary<string, string>>(await response.Content.ReadAsStringAsync())
            ?? throw new UserReportedException("Failed to parse response");
        if (!json.TryGetValue("Id", out var id))
            throw new UserReportedException("Added backup but failed to get from response");

        return await GetBackup(id);
    }

    /// <summary>
    /// Exports a backup
    /// </summary>
    /// <param name="backupId">The ID of the backup</param>
    /// <param name="passphrase">The passphrase to use, if encrypting</param>
    /// <param name="includeKeys">Whether to include sensitive keys</param>
    /// <returns>The stream with the exported backup</returns>
    public async Task<Stream> ExportBackup(string backupId, string? passphrase, bool includeKeys)
    {
        var exportTokenResponse = await client.PostAsync("auth/issuetoken/export", null);
        await EnsureSuccessStatusCodeWithParsing(exportTokenResponse);
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(await exportTokenResponse.Content.ReadAsStringAsync())
            ?? throw new UserReportedException("Failed to parse response");

        if (!values.TryGetValue("Token", out var token))
            throw new UserReportedException("Failed to get export token");

        var response = await client.GetAsync($"backup/{Uri.EscapeDataString(backupId)}/export?passphrase={Uri.EscapeDataString(passphrase ?? "")}&export-passwords={includeKeys}&token={token}");
        await EnsureSuccessStatusCodeWithParsing(response);

        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>
    /// Creates a forever token
    /// </summary>
    /// <returns>The token</returns>
    public async Task<string> CreateForeverToken()
    {
        var (accessToken, _, _) = await ParseAuthResponse(client.PostAsync("auth/issue-forever-token", null));
        return accessToken;
    }


    /// <summary>
    /// The server error structure for JSON deserialization
    /// </summary>
    /// <param name="Error">The error message</param>
    /// <param name="Code">The error code</param>
    private sealed record ServerError(string Error, int Code);

    /// <summary>
    /// Ensures the response is successful or extracts an error message
    /// </summary>
    /// <param name="message">The message to check</param>
    /// <returns>The task</returns>
    private static async Task EnsureSuccessStatusCodeWithParsing(HttpResponseMessage? message)
    {
        if (message is null)
            throw new UserReportedException("No response received");

        if (message.IsSuccessStatusCode)
            return;

        var content = await message.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            message.EnsureSuccessStatusCode();

        ServerError? errMsg = null;
        try
        {
            errMsg = JsonSerializer.Deserialize<ServerError>(content);
        }
        catch
        {
        }

        if (errMsg is not null)
            throw new UserReportedException($"Server error ({errMsg.Code}): {errMsg.Error}");

        throw new UserReportedException($"Failed to parse response ({message.StatusCode}): {content}");
    }
}
