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
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Dto.V2;
using Duplicati.WebserverCore.Endpoints.V1.Backup;

namespace Duplicati.WebserverCore.Client;

/// <summary>
/// A client for interacting with the Duplicati server API, supporting both v1 and v2 endpoints.
/// </summary>
public class DuplicatiServerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;
    private readonly  bool _selfOwnedHttpClient;
    private readonly SemaphoreSlim _tokenRefreshSemaphore = new(1, 1);
    private readonly ServerCredentialType _credentialType;
    private readonly string _credential;

    /// <summary>
    /// Gets a value indicating whether the client is authenticated.
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_httpClient.DefaultRequestHeaders.Authorization?.Parameter);

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicatiServerClient"/> class.
    /// </summary>
    /// <param name="baseUrl">The base URL of the Duplicati server.</param>
    /// <param name="credentialType">The type of credential being provided (Password or Token).</param>
    /// <param name="credential">The server password or access token.</param>
    /// <param name="httpClient">Optional HttpClient instance. If not provided, a new one will be created.</param>
    public DuplicatiServerClient(string baseUrl, ServerCredentialType credentialType, string credential, HttpClient? httpClient = null)
    {
        _selfOwnedHttpClient = httpClient is not null;
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true
        };
        _credential = credential;
        _credentialType = credentialType;
    }

    /// <summary>
    /// Authenticates the client with the Duplicati server using the provided credentials and acquires a bearer token.
    ///
    /// If a call is made to another method before calling Authenticate, it will automatically call this method to ensure the client is authenticated.
    /// The idea of having a separate Authenticate method is to allow for explicit authentication to avoid getting a 401 result on the server log.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    public async Task Authenticate(CancellationToken cancellationToken = default)
    {
        await RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Acquires a bearer token using password-based authentication.
    /// </summary>
    /// <param name="password">The server password.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task AcquireBearerViaPasswordAuthAsync(string password, CancellationToken cancellationToken)
    {
        var loginResult = await LoginV1Async(new LoginInputDto(password, true), cancellationToken).ConfigureAwait(false);
        SetTokenAuthentication(loginResult.AccessToken);
    }

    /// <summary>
    /// Acquires a bearer token using token-based authentication.
    /// </summary>
    /// <param name="token">The signin token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task AcquireBearerViaTokenAuthAsync(string token, CancellationToken cancellationToken)
    {
        var signinResult = await SigninV1Async(new SigninInputDto(token, true), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(signinResult.AccessToken))
            SetTokenAuthentication(signinResult.AccessToken);
    }

    /// <summary>
    /// Sets token-based authentication.
    /// </summary>
    /// <param name="token">The authentication token.</param>
    public void SetTokenAuthentication(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token cannot be null or empty");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Makes an HTTP GET request to the specified endpoint.
    /// </summary>
    /// <typeparam name="T">The type of the response data.</typeparam>
    /// <param name="endpoint">The API endpoint.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <param name="retryOn401">Whether to retry on 401 unauthorized responses.</param>
    /// <returns>The response data.</returns>
    private async Task<T> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default, bool retryOn401 = true)
    {
        return await ExecuteWithRetryAsync<T>(async () =>
        {
            using var response = await _httpClient.GetAsync($"{_baseUrl}{endpoint}", cancellationToken).ConfigureAwait(false);
            return await ProcessResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
        }, retryOn401, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes an HTTP POST request to the specified endpoint.
    /// </summary>
    /// <typeparam name="T">The type of the response data.</typeparam>
    /// <param name="endpoint">The API endpoint.</param>
    /// <param name="data">The request data.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <param name="retryOn401">Whether to retry on 401 unauthorized responses.</param>
    /// <returns>The response data.</returns>
    private async Task<T> PostAsync<T>(string endpoint, object? data, CancellationToken cancellationToken = default, bool retryOn401 = true)
    {
        return await ExecuteWithRetryAsync<T>(async () =>
        {
            var json = data != null ? JsonSerializer.Serialize(data, _jsonOptions) : string.Empty;
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"{_baseUrl}{endpoint}", content, cancellationToken).ConfigureAwait(false);
            return await ProcessResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
        }, retryOn401, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes an HTTP PUT request to the specified endpoint.
    /// </summary>
    /// <typeparam name="T">The type of the response data.</typeparam>
    /// <param name="endpoint">The API endpoint.</param>
    /// <param name="data">The request data.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <param name="retryOn401">Whether to retry on 401 unauthorized responses.</param>
    /// <returns>The response data.</returns>
    private async Task<T> PutAsync<T>(string endpoint, object? data, CancellationToken cancellationToken = default, bool retryOn401 = true)
    {
        return await ExecuteWithRetryAsync<T>(async () =>
        {
            var json = data != null ? JsonSerializer.Serialize(data, _jsonOptions) : string.Empty;
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PutAsync($"{_baseUrl}{endpoint}", content, cancellationToken).ConfigureAwait(false);
            return await ProcessResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
        }, retryOn401, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes an HTTP DELETE request to the specified endpoint.
    /// </summary>
    /// <typeparam name="T">The type of the response data.</typeparam>
    /// <param name="endpoint">The API endpoint.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <param name="retryOn401">Whether to retry on 401 unauthorized responses.</param>
    /// <returns>The response data.</returns>
    private async Task<T> DeleteAsync<T>(string endpoint, CancellationToken cancellationToken = default, bool retryOn401 = true)
    {
        return await ExecuteWithRetryAsync<T>(async () =>
        {
            using var response = await _httpClient.DeleteAsync($"{_baseUrl}{endpoint}", cancellationToken).ConfigureAwait(false);
            return await ProcessResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
        }, retryOn401, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes an HTTP response and handles potential errors.
    /// </summary>
    /// <typeparam name="T">The type of the response data.</typeparam>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The response data.</returns>
    private async Task<T> ProcessResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Unauthorized access - token may be expired");
        }

        response.EnsureSuccessStatusCode();

        if (typeof(T) == typeof(Stream))
        {
            // Response is a stream, read the response into it
            var ms = new MemoryStream();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            ms.Position = 0; // rewind
            return (T)(object)ms;
        }
        else
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(content)!;
        }
    }

    /// <summary>
    /// Executes an HTTP request with retry logic for 401 unauthorized responses.
    /// </summary>
    /// <typeparam name="T">The type of the response data.</typeparam>
    /// <param name="operation">The HTTP operation to execute.</param>
    /// <param name="retryOn401">Whether to retry on 401 unauthorized responses.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The response data.</returns>
    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, bool retryOn401, CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        var attempts = 0;

        while (attempts <= maxRetries)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (retryOn401 && attempts < maxRetries)
            {
                attempts++;
                await RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // This should never be reached due to the loop logic, but included for completeness
        throw new UnauthorizedAccessException("Maximum retry attempts reached for token refresh");
    }

    /// <summary>
    /// Refreshes the authentication token based on the credential type.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    private async Task RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        await _tokenRefreshSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            switch (_credentialType)
            {
                case ServerCredentialType.Password:
                    await AcquireBearerViaPasswordAuthAsync(_credential, cancellationToken).ConfigureAwait(false);
                    break;
                case ServerCredentialType.Token:
                    await AcquireBearerViaTokenAuthAsync(_credential, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported credential type: {_credentialType}");
            }
        }
        finally
        {
            _tokenRefreshSemaphore.Release();
        }
    }

    // V1 Authentication Methods

    /// <summary>
    /// Performs password-based login to the Duplicati server (V1).
    /// </summary>
    /// <param name="input">The login input containing password and remember me flag.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The access token output.</returns>
    public async Task<AccessTokenOutputDto> LoginV1Async(LoginInputDto input, CancellationToken cancellationToken = default)
    {
        return await PostAsync<AccessTokenOutputDto>("/api/v1/auth/login", input, cancellationToken, false).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs token-based signin to the Duplicati server (V1).
    /// </summary>
    /// <param name="input">The signin input containing signin token and remember me flag.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The access token output.</returns>
    public async Task<AccessTokenOutputDto> SigninV1Async(SigninInputDto input, CancellationToken cancellationToken = default)
    {
        return await PostAsync<AccessTokenOutputDto>("/api/v1/auth/signin", input, cancellationToken, false).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the access token using the refresh token (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The new access token output.</returns>
    public async Task<AccessTokenOutputDto> RefreshTokenV1Async(CancellationToken cancellationToken = default)
    {
        return await PostAsync<AccessTokenOutputDto>("/api/v1/auth/refresh", null, cancellationToken, false).ConfigureAwait(false);
    }

    /// <summary>
    /// Issues a signin token for authentication (V1).
    /// </summary>
    /// <param name="input">The signin token input.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The signin token output.</returns>
    public async Task<SigninTokenOutputDto> IssueSigninTokenV1Async(IssueSigninTokenInputDto input, CancellationToken cancellationToken = default)
    {
        return await PostAsync<SigninTokenOutputDto>("/api/v1/auth/issuesignintoken", input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Issues a single-operation token for a specific operation (V1).
    /// </summary>
    /// <param name="operation">The operation name.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The single operation token output.</returns>
    public async Task<SingleOperationTokenOutputDto> IssueTokenV1Async(string operation, CancellationToken cancellationToken = default)
    {
        return await PostAsync<SingleOperationTokenOutputDto>($"/api/v1/auth/issuetoken/{operation}", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Issues a forever token for long-term authentication (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The single operation token output.</returns>
    public async Task<SingleOperationTokenOutputDto> IssueForeverTokenV1Async(CancellationToken cancellationToken = default)
    {
        return await PostAsync<SingleOperationTokenOutputDto>("/api/v1/auth/issue-forever-token", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs out and invalidates the refresh token (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LogoutV1Async(CancellationToken cancellationToken = default)
    {
        await PostAsync<object>("/api/v1/auth/refresh/logout", null, cancellationToken).ConfigureAwait(false);
    }

    // V1 Backup Management Methods

    /// <summary>
    /// Lists all backups configured on the server (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The list of backups.</returns>
    public async Task<BackupAndScheduleOutputDto[]> ListBackupsV1Async(CancellationToken cancellationToken = default)
    {
        return await GetAsync<BackupAndScheduleOutputDto[]>("/api/v1/backups", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new backup configuration (V1).
    /// </summary>
    /// <param name="backup">The backup configuration to create.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The created backup configuration.</returns>
    public async Task<BackupDto> CreateBackupV1Async(BackupDto backup, CancellationToken cancellationToken = default)
    {
        return await PostAsync<BackupDto>("/api/v1/backups", backup, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports a backup configuration (V1).
    /// </summary>
    /// <param name="input">The import backup input.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The import backup output.</returns>
    public async Task<ImportBackupOutputDto> ImportBackupV1Async(ImportBackupInputDto input, CancellationToken cancellationToken = default)
    {
        return await PostAsync<ImportBackupOutputDto>("/api/v1/backups/import", input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets details of a specific backup (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The backup details.</returns>
    public async Task<BackupGet.GetBackupResultDto> GetBackupV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await GetAsync<BackupGet.GetBackupResultDto>($"/api/v1/backup/{backupId}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a backup configuration (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="backup">The updated backup configuration.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The updated backup configuration.</returns>
    public async Task<BackupDto> UpdateBackupV1Async(string backupId, BackupDto backup, CancellationToken cancellationToken = default)
    {
        return await PutAsync<BackupDto>($"/api/v1/backup/{backupId}", backup, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a backup configuration (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The delete backup output.</returns>
    public async Task<DeleteBackupOutputDto> DeleteBackupV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync<DeleteBackupOutputDto>($"/api/v1/backup/{backupId}", cancellationToken).ConfigureAwait(false);
    }

    // V1 Backup Operations

    /// <summary>
    /// Starts a backup operation (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> StartBackupV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/start", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a backup operation (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> RunBackupV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/run", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Restores files from a backup (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="input">The restore input parameters.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> RestoreBackupV1Async(string backupId, RestoreInputDto input, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/restore", input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies a backup's integrity (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> VerifyBackupV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/verify", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Repairs a backup (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="input">The repair input parameters.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> RepairBackupV1Async(string backupId, RepairInputDto input, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/repair", input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Repairs and updates a backup (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> RepairUpdateBackupV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/repairupdate", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Compacts a backup (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> CompactBackupV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/compact", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Vacuums a backup database (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> VacuumBackupV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/vacuum", null, cancellationToken).ConfigureAwait(false);
    }

    // V1 Backup Data Access

    /// <summary>
    /// Lists files in a backup (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The list of files.</returns>
    public async Task<TreeNodeDto[]> ListFilesV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await GetAsync<TreeNodeDto[]>($"/api/v1/backup/{backupId}/files", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists backup filesets (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The list of filesets.</returns>
    public async Task<object[]> ListFilesetsV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await GetAsync<object[]>($"/api/v1/backup/{backupId}/filesets", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the backup log (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The backup log entries.</returns>
    public async Task<LogEntry[]> GetBackupLogV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await GetAsync<LogEntry[]>($"/api/v1/backup/{backupId}/log", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the remote operation log (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The remote log entries.</returns>
    public async Task<LogEntry[]> GetRemoteLogV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await GetAsync<LogEntry[]>($"/api/v1/backup/{backupId}/remotelog", cancellationToken).ConfigureAwait(false);
    }

    // V1 Database Management

    /// <summary>
    /// Deletes a backup database (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> DeleteDatabaseV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/deletedb", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a backup database (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="input">The database path input.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> MoveDatabaseV1Async(string backupId, UpdateDbPathInputDto input, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/movedb", input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the database path (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="input">The database path input.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task started information.</returns>
    public async Task<TaskStartedDto> UpdateDatabaseV1Async(string backupId, UpdateDbPathInputDto input, CancellationToken cancellationToken = default)
    {
        return await PostAsync<TaskStartedDto>($"/api/v1/backup/{backupId}/updatedb", input, cancellationToken).ConfigureAwait(false);
    }

    // V1 Export Operations

    /// <summary>
    /// Exports a backup configuration (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The backup configuration.</returns>
    public async Task<Stream> ExportBackupV1Async(string backupId, bool exportPasswords, string passPhrase, string exportToken, CancellationToken cancellationToken = default)
    {

        // [FromRoute] string id, [FromQuery(Name = "export-passwords")] bool? exportPasswords, [FromQuery] string? passphrase, [FromQuery] string token
        return await GetAsync<Stream>($"/api/v1/backup/{backupId}/export?exportpasswords={exportPasswords}&passphrase={Uri.EscapeDataString(passPhrase)}&token={Uri.EscapeDataString(exportToken)}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Exports backup as command line (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The command line export.</returns>
    public async Task<ExportCommandlineDto> ExportCommandlineV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await GetAsync<ExportCommandlineDto>($"/api/v1/backup/{backupId}/export-cmdline", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Exports backup arguments only (V1).
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The arguments export.</returns>
    public async Task<ExportArgsOnlyDto> ExportArgsOnlyV1Async(string backupId, CancellationToken cancellationToken = default)
    {
        return await GetAsync<ExportArgsOnlyDto>($"/api/v1/backup/{backupId}/export-argsonly", cancellationToken).ConfigureAwait(false);
    }

    // V1 Server Management

    /// <summary>
    /// Gets the server state (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The server status.</returns>
    public async Task<ServerStatusDto> GetServerStateV1Async(CancellationToken cancellationToken = default)
    {
        return await GetAsync<ServerStatusDto>("/api/v1/serverstate", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pauses the server (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PauseServerV1Async(CancellationToken cancellationToken = default)
    {
        await PostAsync<object>("/api/v1/serverstate/pause", null, cancellationToken);
    }

    /// <summary>
    /// Resumes the server (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ResumeServerV1Async(CancellationToken cancellationToken = default)
    {
        await PostAsync<object>("/api/v1/serverstate/resume", null, cancellationToken);
    }

    // V1 Task Management

    /// <summary>
    /// Lists all active tasks (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The list of active tasks.</returns>
    public async Task<object[]> ListTasksV1Async(CancellationToken cancellationToken = default)
    {
        return await GetAsync<object[]>("/api/v1/tasks", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets details of a specific task (V1).
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The task details.</returns>
    public async Task<GetTaskStateDto> GetTaskV1Async(string taskId, CancellationToken cancellationToken = default)
    {
        return await GetAsync<GetTaskStateDto>($"/api/v1/task/{taskId}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops a running task (V1).
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopTaskV1Async(string taskId, CancellationToken cancellationToken = default)
    {
        await PostAsync<object>($"/api/v1/task/{taskId}/stop", null, cancellationToken);
    }

    /// <summary>
    /// Aborts a running task (V1).
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AbortTaskV1Async(string taskId, CancellationToken cancellationToken = default)
    {
        await PostAsync<object>($"/api/v1/task/{taskId}/abort", null, cancellationToken);
    }

    // V1 System Information

    /// <summary>
    /// Gets system information (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The system information.</returns>
    public async Task<SystemInfoDto> GetSystemInfoV1Async(CancellationToken cancellationToken = default)
    {
        return await GetAsync<SystemInfoDto>("/api/v1/systeminfo", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the changelog (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The changelog entries.</returns>
    public async Task<ChangelogDto[]> GetChangelogV1Async(CancellationToken cancellationToken = default)
    {
        return await GetAsync<ChangelogDto[]>("/api/v1/changelog", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets license information (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The license information.</returns>
    public async Task<LicenseDto[]> GetLicensesV1Async(CancellationToken cancellationToken = default)
    {
        return await GetAsync<LicenseDto[]>("/api/v1/licenses", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets acknowledgements (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The acknowledgements.</returns>
    public async Task<AcknowlegdementDto[]> GetAcknowledgementsV1Async(CancellationToken cancellationToken = default)
    {
        return await GetAsync<AcknowlegdementDto[]>("/api/v1/acknowledgements", cancellationToken).ConfigureAwait(false);
    }

    // V1 Settings Management

    /// <summary>
    /// Gets server settings (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The server settings.</returns>
    public async Task<SettingDto[]> GetServerSettingsV1Async(CancellationToken cancellationToken = default)
    {
        return await GetAsync<SettingDto[]>("/api/v1/serversetting", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates server settings (V1).
    /// </summary>
    /// <param name="settings">The settings to update.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The updated settings.</returns>
    public async Task<SettingDto[]> UpdateServerSettingsV1Async(SettingDto[] settings, CancellationToken cancellationToken = default)
    {
        return await PutAsync<SettingDto[]>("/api/v1/serversetting", settings, cancellationToken).ConfigureAwait(false);
    }

    // V1 Filesystem Operations

    /// <summary>
    /// Browses the filesystem (V1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The filesystem entries.</returns>
    public async Task<TreeNodeDto[]> BrowseFilesystemV1Async(CancellationToken cancellationToken = default)
    {
        return await GetAsync<TreeNodeDto[]>("/api/v1/filesystem", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs filesystem operations (V1).
    /// </summary>
    /// <param name="data">The filesystem operation data.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The operation result.</returns>
    public async Task<object> PerformFilesystemOperationV1Async(object data, CancellationToken cancellationToken = default)
    {
        return await PostAsync<object>("/api/v1/filesystem", data, cancellationToken).ConfigureAwait(false);
    }

    // V2 API Methods

    /// <summary>
    /// Lists filesets with pagination (V2).
    /// </summary>
    /// <param name="request">The list filesets request.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The paged filesets response.</returns>
    public async Task<PagedResponseEnvelope<ListFilesetsResponseDto>> ListFilesetsV2Async(ListFilesetsRequestDto request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<PagedResponseEnvelope<ListFilesetsResponseDto>>("/api/v2/backup/list-filesets", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists folder contents with pagination (V2).
    /// </summary>
    /// <param name="request">The list folder content request.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The paged folder content response.</returns>
    public async Task<PagedResponseEnvelope<ListFolderContentResponseDto>> ListFolderContentV2Async(ListFolderContentRequestDto request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<PagedResponseEnvelope<ListFolderContentResponseDto>>("/api/v2/backup/list-folder", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists file versions with pagination (V2).
    /// </summary>
    /// <param name="request">The list file versions request.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The paged file versions response.</returns>
    public async Task<PagedResponseEnvelope<ListFileVersionsOutputDto>> ListFileVersionsV2Async(ListFileVersionsRequestDto request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<PagedResponseEnvelope<ListFileVersionsOutputDto>>("/api/v2/backup/list-versions", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches entries with filters (V2).
    /// </summary>
    /// <param name="request">The search entries request.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The paged search results response.</returns>
    public async Task<PagedResponseEnvelope<SearchEntriesResponseDto>> SearchEntriesV2Async(SearchEntriesRequestDto request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<PagedResponseEnvelope<SearchEntriesResponseDto>>("/api/v2/backup/search", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tests destination connectivity (V2).
    /// </summary>
    /// <param name="request">The destination test request.</param>
    /// <param name="cancellationToken">The cancellation token. Optional, defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>The destination test response.</returns>
    public async Task<ResponseEnvelope<DestinationTestResponseDto>> TestDestinationV2Async(DestinationTestRequestDto request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<ResponseEnvelope<DestinationTestResponseDto>>("/api/v2/destination/test", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases all resources used by the <see cref="DuplicatiServerClient"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="DuplicatiServerClient"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            if (_selfOwnedHttpClient)
                _httpClient?.Dispose();
            _tokenRefreshSemaphore?.Dispose();
        }
        _disposed = true;
    }
}
