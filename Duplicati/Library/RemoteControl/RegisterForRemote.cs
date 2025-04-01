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
using System.Text.Json;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

namespace Duplicati.Library.RemoteControl;

/// <summary>
/// Implementation of the remote control enrollment process
/// </summary>
public class RegisterForRemote : IDisposable
{
    /// <summary>
    /// The log tag for messages from this class
    /// </summary>
    private static readonly string LogTag = Log.LogTagFromType<RegisterForRemote>();

    /// <summary>
    /// The interval between retries when registering the machine
    /// </summary>
    private static readonly TimeSpan ClientRegisterRetryInterval = TimeSpan.FromSeconds(30);
    /// <summary>
    /// The maximum number of retries when registering the machine
    /// </summary>
    private static readonly int ClientRegisterMaxRetries = 10;

    /// <summary>
    /// A random ID for this client instance
    /// </summary>
    private static readonly string ClientInstanceId = Guid.NewGuid().ToString();

    /// <summary>
    /// The default URL to register the machine with
    /// </summary>
    public static string DefaultRegisterationUrl
    {
        get
        {
            var envvalue = Environment.GetEnvironmentVariable("DUPLICATI_REMOTE_CONTROL_URL");
            return string.IsNullOrWhiteSpace(envvalue)
                ? "https://api.duplicati.com/remotecontrol/register"
                : envvalue;
        }
    }

    /// <summary>
    /// The Json options to use for serialization
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// The states that the process can be in
    /// </summary>
    public enum States
    {
        /// <summary>
        /// The process has not started
        /// </summary>
        NotStarted,
        /// <summary>
        /// The machine is being registered
        /// </summary>
        Registering,
        /// <summary>
        /// The machine has been registered
        /// </summary>
        Registered,
        /// <summary>
        /// The machine is waiting for the user to claim it
        /// </summary>
        WaitingForClaim,
        /// <summary>
        /// The machine has been claimed
        /// </summary>
        Claimed,
        /// <summary>
        /// The process has failed
        /// </summary>
        Failed,
        /// <summary>
        /// The process has been disposed
        /// </summary>
        Disposed
    }

    /// <summary>
    /// Data returned when the machine is claimed
    /// </summary>
    /// <param name="Success">True if the claim was successful</param>
    /// <param name="StatusMessage">The status message for the claim</param>
    /// <param name="JWT">The JWT token for the machine</param>
    /// <param name="ServerUrl">The URL for the remote server</param>
    /// <param name="CertificateUrl">The URL for getting new server certificates</param>
    /// <param name="ServerCertificates">The certificates for the remote server</param>
    /// <param name="LocalEncryptionKey">The encryption key for the local settings</param>
    private sealed record EnvelopedClaimedClientData(
        bool Success,
        string StatusMessage,
        string JWT,
        string ServerUrl,
        string CertificateUrl,
        IEnumerable<MiniServerCertificate> ServerCertificates,
        string? LocalEncryptionKey
    );

    /// <summary>
    /// The current state of the registration process
    /// </summary>
    private States _state;
    /// <summary>
    /// The URL to register the machine with
    /// </summary>
    private readonly string _registrationUrl;
    /// <summary>
    /// The cancellation token source for the process
    /// </summary>
    private readonly CancellationTokenSource _cancellationTokenSource;
    /// <summary>
    /// The HTTP client to use for requests
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// The data returned when registering the machine
    /// </summary>
    private RegisterClientData? _registerClientData;
    /// <summary>
    /// The data returned when claiming the machine
    /// </summary>
    private ClaimedClientData? _claimedClientData;

    /// <summary>
    /// Creates a new instance of the registration process
    /// </summary>
    /// <param name="registrationUrl">The URL to register the machine with</param>
    /// <param name="httpClient">The HTTP client to use for requests</param>
    /// <param name="cancellationToken">The cancellation token to use for the process</param>
    public RegisterForRemote(string registrationUrl, HttpClient? httpClient, CancellationToken cancellationToken)
    {
        _state = States.NotStarted;
        _registrationUrl = registrationUrl;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// The current state of the registration process
    /// </summary>
    public States State => _state;

    /// <summary>
    /// Starts the registration process and returns the data needed to claim the machine
    /// </summary>
    /// <param name="maxRetries">The maximum number of retries when registering the machine</param>
    /// <param name="retryInterval">The interval between retries when registering the machine</param>
    /// <returns>The data needed to claim the machine</returns>
    /// <exception cref="InvalidOperationException">Thrown if the class is not in the correct state</exception>
    /// <exception cref="Exception">Thrown if the machine could not be registered</exception>
    public async Task<(RegisterClientData? RegistrationData, ClaimedClientData? ClaimedData)> Register(int? maxRetries = null, TimeSpan? retryInterval = null)
    {
        if (_state != States.NotStarted)
            throw new InvalidOperationException("Registration process has already started");

        _state = States.Registering;
        try
        {
            (_registerClientData, _claimedClientData) = await RetryHelper.Retry(() => RegisterClient(), maxRetries ?? ClientRegisterMaxRetries, retryInterval ?? ClientRegisterRetryInterval, _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            if (_state != States.Disposed)
                _state = States.Failed;
            Log.WriteMessage(LogMessageType.Error, LogTag, "ClientRegistrationFailed", ex, $"Failed to register machine with server: {_registrationUrl}");
            throw;
        }

        if (_state != States.Disposed)
        {
            if (_claimedClientData != null)
                _state = States.Claimed;
            else
                _state = States.Registered;
        }

        return (_registerClientData, _claimedClientData);
    }

    /// <summary>
    /// Creates the machine data content for registration
    /// </summary>
    /// <returns>The JSON content for the machine data</returns>
    private JsonContent CreateMachineData()
    {
        var basics = new Dictionary<string, string?>
        {
            { "instanceId", ClientInstanceId },
            { "machineId", AutoUpdater.DataFolderManager.MachineID },
            { "machineName", AutoUpdater.DataFolderManager.MachineName },
            { "installId", AutoUpdater.DataFolderManager.InstallID },
            { "localTime", DateTimeOffset.Now.ToString("o") },
            { "version", AutoUpdater.UpdaterManager.SelfVersion?.Version },
            { "packageTypeId", AutoUpdater.UpdaterManager.PackageTypeId },
            { "operatingSystem", AutoUpdater.UpdaterManager.OperatingSystemName }
        };

        // Pass any query parameters from the registration URL into the JSON body
        // so they url can override the defaults if needed
        var uri = new System.Uri(_registrationUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        foreach (var key in query.AllKeys)
            if (key != null && !string.IsNullOrWhiteSpace(query[key]))
                basics[key] = query[key];

        return JsonContent.Create(basics);
    }

    /// <summary>
    /// Registers the machine with the server
    /// </summary>
    /// <returns>The data needed to claim the machine</returns>
    private async Task<(RegisterClientData?, ClaimedClientData?)> RegisterClient()
    {
        var response = await _httpClient.PostAsync(_registrationUrl, CreateMachineData(), _cancellationTokenSource.Token);

        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync(_cancellationTokenSource.Token);
        if (string.IsNullOrWhiteSpace(raw))
            throw new Exception("Failed to read client registration data");

        var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("jwt", out var _) && doc.RootElement.TryGetProperty("serverUrl", out var _))
        {
            return JsonSerializer.Deserialize<ClaimedClientData>(raw, JsonOptions) switch
            {
                { } data => (null, data),
                null => throw new Exception("Failed to read client claimed data")
            };
        }

        if (doc.RootElement.TryGetProperty("claimLink", out var _) && doc.RootElement.TryGetProperty("statusLink", out var _))
        {
            return JsonSerializer.Deserialize<RegisterClientData>(raw, JsonOptions) switch
            {
                { } data => (data, null),
                null => throw new Exception("Failed to read client registration data")
            };
        }

        throw new Exception("Failed to read client registration data");
    }

    /// <summary>
    /// Claims the machine with the server
    /// </summary>
    /// <returns>The settings for the machine</returns>
    /// <exception cref="InvalidOperationException">Thrown if the class is not in the correct state</exception>
    /// <exception cref="Exception">Thrown if the machine could not be registered</exception>
    public async Task<ClaimedClientData> Claim()
    {
        if (_state == States.Claimed)
            return _claimedClientData ?? throw new InvalidOperationException("Claimed data is null");

        if (_state != States.Registered)
            throw new InvalidOperationException("Resgistration process is not in the registered state");

        _state = States.WaitingForClaim;
        _cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(Math.Min(3600, Math.Max(1, _registerClientData!.MaxLifetimeSeconds))));
        try
        {
            _claimedClientData = await RetryHelper.Retry(() => CheckClientClaimed(), Math.Min(100, Math.Max(1, _registerClientData!.MaxRetries)), TimeSpan.FromSeconds(_registerClientData!.RetrySeconds), _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            if (_state != States.Disposed)
                _state = States.Failed;
            Log.WriteMessage(LogMessageType.Error, LogTag, "ClientClaimFailed", ex, "Failed to claim machine with server");
            throw;
        }

        _state = States.Claimed;
        return _claimedClientData;
    }

    /// <summary>
    /// Checks if the machine has been claimed
    /// </summary>
    /// <returns>The data for the claimed machine</returns>
    /// <exception cref="Exception">Thrown if the machine could not be claimed</exception>
    private async Task<ClaimedClientData> CheckClientClaimed()
    {
        var response = await _httpClient.PostAsync(_registerClientData!.StatusLink, CreateMachineData(), _cancellationTokenSource.Token);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EnvelopedClaimedClientData>(options: JsonOptions, _cancellationTokenSource.Token)
            ?? throw new Exception("Failed to read machine claim data");

        if (!result.Success)
            throw new Exception($"Failed to claim machine: {result.StatusMessage}");

        return new ClaimedClientData(result.JWT, result.ServerUrl, result.CertificateUrl, result.ServerCertificates, result.LocalEncryptionKey);
    }

    /// </inheritdoc>
    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _httpClient.Dispose();
        _state = States.Disposed;
    }
}