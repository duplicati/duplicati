using System.Net.Http.Json;
using System.Text.Json;

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
        Dictionary<string, string>? Metadata
    );

    /// <summary>
    /// The response backup data returned from the server
    /// </summary>
    /// <param name="Backup">The backup details</param>
    private sealed record ResponseBackupEntry(ResponseBackupEntry.ResponseBackupDetailsEntry Backup)
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
            => new BackupEntry(Backup.ID, Backup.Name, Backup.Description ?? "", Backup.Metadata);
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
    /// <returns>The connection</returns>
    public static async Task<Connection> Connect(Settings settings, bool obtainRefreshToken = false)
    {
        Console.WriteLine($"Connecting to {settings.HostUrl}...");

        // Configure the client for requests
        var client = new HttpClient()
        {
            BaseAddress = new Uri(settings.HostUrl + "api/v1/"),
        };

        // If we already have a refresh token, try that first
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.RefreshToken))
            {
                var (accessToken, refreshToken) = await LoginWithRefreshToken(client, settings.RefreshToken);
                if (string.IsNullOrWhiteSpace(accessToken))
                    throw new InvalidOperationException("Failed to get access token");
                if (string.IsNullOrWhiteSpace(refreshToken))
                    throw new InvalidOperationException("Failed to get refresh token");

                (settings with { RefreshToken = refreshToken }).Save();
                return CreateConnectionWithClient(client, accessToken);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to use refresh token: {ex.Message}");
        }

        // If we can read the server database, try to create a signin token
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.ServerDatafolder) && File.Exists(Path.Combine(settings.ServerDatafolder, "Duplicati-server.sqlite")))
            {
                string? cfg = null;
                using (var connection = Duplicati.Server.Program.GetDatabaseConnection(new Dictionary<string, string> { { "server-datafolder", settings.ServerDatafolder } }))
                    cfg = connection.ApplicationSettings.JWTConfig;

                if (!string.IsNullOrWhiteSpace(cfg))
                {
                    var signinjwt = new WebserverCore.Middlewares.JWTTokenProvider(
                        JsonSerializer.Deserialize<WebserverCore.Middlewares.JWTConfig>(cfg)
                            ?? throw new InvalidOperationException("Failed to deserialize JWTConfig")
                    ).CreateSigninToken("server-cli");

                    var response = await client.PostAsync("auth/signin", JsonContent.Create(new { SigninToken = signinjwt, RememberMe = obtainRefreshToken }));
                    var (accessToken, refreshToken) = ParseAuthResponse(response);
                    if (string.IsNullOrWhiteSpace(accessToken))
                        throw new InvalidOperationException("Failed to get access token");

                    if (!string.IsNullOrWhiteSpace(refreshToken))
                        (settings with { RefreshToken = refreshToken }).Save();

                    return CreateConnectionWithClient(client, accessToken);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to obtain a signin token: {ex.Message}");
        }

        // Otherwise, we need a password to log in
        try
        {
            // Try obtaining the password from the user
            if (string.IsNullOrWhiteSpace(settings.Password))
                settings = settings with { Password = HelperMethods.ReadPasswordFromConsole("Enter server password: ") };

            if (string.IsNullOrWhiteSpace(settings.Password))
                throw new UserReportedException("Password is required");

            var (accessToken, refreshToken) = await LoginWithPassword(client, settings.Password, obtainRefreshToken);
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("Failed to get access token");

            if (!string.IsNullOrWhiteSpace(refreshToken))
                (settings with { RefreshToken = refreshToken }).Save();

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
    private static async Task<(string? AccessToken, string? RefreshToken)> LoginWithPassword(HttpClient client, string password, bool obtainRefreshToken)
        => ParseAuthResponse(
            await client.PostAsync($"auth/login", JsonContent.Create(new { Password = password, RememberMe = obtainRefreshToken }))
        );

    /// <summary>
    /// Logs in with a refresh token
    /// </summary>
    /// <param name="client">The HTTP client</param>
    /// <param name="refreshToken">The refresh token to use</param>
    /// <returns>The access and refresh tokens</returns>
    private static async Task<(string? AccessToken, string? RefreshToken)> LoginWithRefreshToken(HttpClient client, string refreshToken)
        => ParseAuthResponse(await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "auth/refresh")
        {
            Headers = { { "Cookie", $"RefreshToken_{client.BaseAddress!.Port}={refreshToken}" } },
        }));


    /// <summary>
    /// Parses the authentication response
    /// </summary>
    /// <param name="response">The response to parse</param>
    /// <returns>The access and refresh tokens</returns>
    private static (string? AccessToken, string? RefreshToken) ParseAuthResponse(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var json = JsonSerializer.Deserialize<Dictionary<string, string>>(response.Content.ReadAsStringAsync().Result)
            ?? throw new InvalidOperationException("Failed to parse response");

        if (!json.TryGetValue("AccessToken", out var accessToken))
            throw new InvalidOperationException("Failed to get access token");

        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        var refreshToken = cookies?.SelectMany(c => c.Split(';')).FirstOrDefault(c => c.StartsWith($"RefreshToken_"))?.Split('=', 2)[1];

        return (accessToken, refreshToken);
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
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Resumes the server
    /// </summary>
    /// <returns>The task</returns>
    public async Task Resume()
    {
        var response = await client.PostAsync($"serverstate/resume", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Lists the backups configured on the server
    /// </summary>
    /// <returns>The backups</returns>
    public async Task<IEnumerable<BackupEntry>> ListBackups()
    {
        var response = await client.GetAsync("backups");
        response.EnsureSuccessStatusCode();

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
        response.EnsureSuccessStatusCode();

        return (JsonSerializer.Deserialize<ResponseBackupEntry>(await response.Content.ReadAsStringAsync())
            ?? throw new UserReportedException("Failed to parse response"))
            .ToBackupEntry();
    }

    /// <summary>
    /// Runs a backup
    /// </summary>
    /// <param name="backupId">The ID of the backup</param>
    /// <returns>The task</returns>
    public async Task RunBackup(string backupId)
    {
        var response = await client.PostAsync($"backup/{Uri.EscapeDataString(backupId)}/run", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Lists the active tasks
    /// </summary>
    /// <returns>The tasks</returns>
    public async Task<IEnumerable<TaskEntry>> ListTasks()
    {
        var response = await client.GetAsync("tasks");
        response.EnsureSuccessStatusCode();

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
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
        var response = await client.PostAsync($"task/{Uri.EscapeDataString(taskId)}/{levelString}", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Logs out of the server
    /// </summary>
    /// <param name="settings">The settings to use</param>
    /// <returns>The task</returns>
    public async Task Logout(Settings settings)
    {
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "auth/refresh/logout")
        {
            Headers = { { "Cookie", $"RefreshToken_{client.BaseAddress!.Port}={settings.RefreshToken}" } }
        });
        response.EnsureSuccessStatusCode();
        (settings with { RefreshToken = null }).Save();
    }

    /// <summary>
    /// Changes the server password
    /// </summary>
    /// <param name="newPassword">The new password to use</param>
    /// <returns>The task</returns>
    public async Task ChangePassword(string newPassword)
    {
        var response = await client.PutAsync("serversetting/server-passphrase", JsonContent.Create(newPassword));
        response.EnsureSuccessStatusCode();
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
        response.EnsureSuccessStatusCode();

        var json = JsonSerializer.Deserialize<Dictionary<string, string>>(await response.Content.ReadAsStringAsync())
            ?? throw new UserReportedException("Failed to parse response");
        if (!json.TryGetValue("Id", out var id))
            throw new UserReportedException("Added backup but failed to get from response");

        return await GetBackup(id);
    }
}
