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
using System.Security.Cryptography;
using System.Text.Json;
using Duplicati.Library.Logging;

namespace Duplicati.Library.RemoteControl;

/// <summary>
/// Support class for keeping a connection to a remote server
/// </summary>
public class KeepRemoteConnection : IDisposable
{
    /// <summary>
    /// The protocol version to use
    /// </summary>
    private const int PROTOCOL_VERSION = 1;
    /// <summary>
    /// The log tag for messages from this class
    /// </summary>
    private static readonly string LogTag = Log.LogTagFromType<KeepRemoteConnection>();

    /// <summary>
    /// The interval between reconnect attempts
    /// </summary>
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The interval between heartbeats
    /// </summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The time between reconnect attempts if no response is received
    /// </summary>
    private static readonly TimeSpan NoResponseTimeout = HeartbeatInterval * 2;

    /// <summary>
    /// The minimum time between certificate refreshes
    /// </summary>
    private static readonly TimeSpan MinimumCertificateRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The interval between certificate refreshes
    /// </summary>
    private static readonly TimeSpan CertificateRefreshInterval = TimeSpan.FromDays(7);

    /// <summary>
    /// The client key to use for signing messages
    /// </summary>
    private static readonly RSA ClientKey = RSA.Create(2048);

    /// <summary>
    /// The client ID to use for identifying the client
    /// </summary>
    private static readonly string ClientId = Guid.NewGuid().ToString();

    /// <summary>
    /// The JSON options to use for deserialization
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The stats the connection can be in
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>
        /// The connection is not established
        /// </summary>
        NotConnected,
        /// <summary>
        /// We received a welcome message
        /// </summary>
        WelcomeReceived,
        /// <summary>
        /// The connection is authenticated
        /// </summary>
        Authenticated,
        /// <summary>
        /// The connection is in an error state
        /// </summary>
        Error
    }

    /// <summary>
    /// The websocket client
    /// </summary>
    private readonly Websocket.Client.WebsocketClient _client;
    /// <summary>
    /// The cancellation token source
    /// </summary>
    private readonly CancellationTokenSource _cancellationTokenSource;
    /// <summary>
    /// The current state of the connection
    /// </summary>
    private ConnectionState _state = ConnectionState.NotConnected;
    /// <summary>
    /// The task that runs the connection
    /// </summary>
    private Task _runnerTask;
    /// <summary>
    /// The currently negotiated server certificate
    /// </summary>
    private MiniServerCertificate? _serverCertificate;
    /// <summary>
    /// The public key of the server
    /// </summary>
    private RSA? _serverPublicKey;

    /// <summary>
    /// The callback to call when rekeying
    /// </summary>
    private readonly Func<ClaimedClientData, Task> _onReKey;
    /// <summary>
    /// The callback to call when a message is received
    /// </summary>
    private readonly Func<CommandMessage, Task> _onMessage;

    /// <summary>
    /// The current JWT token
    /// </summary>
    private string _token;
    /// <summary>
    /// The server URL
    /// </summary>
    private string _serverUrl;
    /// <summary>
    /// The certificate URL
    /// </summary>
    private string _certificateUrl;
    /// <summary>
    /// The server keys
    /// </summary>
    private IEnumerable<MiniServerCertificate> _serverKeys;
    /// <summary>
    /// The last time a message was received
    /// </summary>
    private DateTimeOffset _lastMessageReceived = DateTimeOffset.MinValue;

    /// <summary>
    /// Creates a new connection to the remote server
    /// </summary>
    /// <param name="serverUrl">The url to use</param>
    /// <param name="JWT">The JWT token to use</param>
    /// <param name="serverKeys">The server keys to use</param>
    /// <param name="cancellationToken">The token to cancel the connection</param>
    private KeepRemoteConnection(string serverUrl, string JWT, string certificateUrl, IEnumerable<MiniServerCertificate> serverKeys, CancellationToken cancellationToken, Func<ClaimedClientData, Task> onReKey, Func<CommandMessage, Task> onMessage)
    {
        _serverUrl = serverUrl;
        _certificateUrl = certificateUrl;
        _token = JWT;
        _serverKeys = serverKeys;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _onReKey = onReKey;
        _onMessage = onMessage;

        _client = new Websocket.Client.WebsocketClient(new Uri(serverUrl));
        _runnerTask = RunMainLoop();
    }

    /// <summary>
    /// Runs the inner loop of the connection
    /// </summary>
    private async Task RunMainLoop()
    {
        _client.ReconnectTimeout = NoResponseTimeout;
        _client.LostReconnectTimeout = ReconnectInterval;
        _client.IsReconnectionEnabled = true;

        // Set up the periodic refreshers
        using var reconnectHelper = new PeriodicRefresher(
            Timeout.InfiniteTimeSpan,
            ReconnectInterval,
            async token =>
            {
                await _client.Start();
            },
            _cancellationTokenSource.Token);

        using var heartbeatHelper = new PeriodicRefresher(
            HeartbeatInterval,
            TimeSpan.FromSeconds(1),
            _ =>
            {
                // Reconnect if we have disconnected
                if (!_client.IsRunning)
                {
                    reconnectHelper.Signal();
                }
                // If we do not get any response from the server, we should reconnect
                else if ((_state == ConnectionState.Authenticated || _state == ConnectionState.WelcomeReceived) && _lastMessageReceived.Add(NoResponseTimeout) < DateTimeOffset.Now)
                {
                    _state = ConnectionState.Error;
                    Log.WriteMessage(LogMessageType.Warning, LogTag, "WebsocketDisconnect", "No response from server");
                    _client.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "No response");
                }

                SendEnvelope(new EnvelopedMessage()
                {
                    From = ClientId,
                    To = "server",
                    Type = "ping",
                    ErrorMessage = null,
                    Payload = null,
                    MessageId = Guid.NewGuid().ToString()
                });

                return Task.CompletedTask;
            },
            _cancellationTokenSource.Token);

        using var certificateRfreshHelper = new PeriodicRefresher(
            CertificateRefreshInterval,
            MinimumCertificateRefreshInterval,
            RefreshCertificates,
            _cancellationTokenSource.Token);

        _client.DisconnectionHappened.Subscribe(info =>
        {
            _state = ConnectionState.NotConnected;
            _serverCertificate = null;
            _serverPublicKey = null;
            reconnectHelper.Signal();
            Log.WriteMessage(LogMessageType.Warning, LogTag, "WebsocketDisconnect", "Disconnected from the server");
        });

        _client.MessageReceived.Subscribe(async msg =>
        {
            // Ignore messages if we are in an error state
            if (_state == ConnectionState.Error)
                return;

            _lastMessageReceived = DateTimeOffset.Now;
            Log.WriteMessage(LogMessageType.Verbose, LogTag, "WebsocketMessage", "Received message from server: {0}", msg);

            try
            {
                if (string.IsNullOrWhiteSpace(msg.Text))
                    throw new ProtocolViolationException("Empty message");

                if (_serverCertificate == null || _serverPublicKey == null || _state == ConnectionState.NotConnected)
                {
                    // Should be safe from replay, as the response is encrypted with the server public key
                    // So even a replay attack would not let the attacker know the client's token
                    var welcomeEnvelope = EnvelopedMessage.ForceParse(msg.Text);
                    if (welcomeEnvelope.GetMessageType() != MessageType.Welcome)
                        throw new ProtocolViolationException("Expected welcome message");
                    if (string.IsNullOrWhiteSpace(welcomeEnvelope.Payload))
                        throw new ProtocolViolationException("No payload in welcome message");

                    var welcomeMessage = welcomeEnvelope.GetPayload<WelcomeMessage>()
                        ?? throw new ProtocolViolationException("Invalid welcome message");

                    if (string.IsNullOrWhiteSpace(welcomeMessage.PublicKeyHash))
                        throw new ProtocolViolationException("No public key hash in welcome message");
                    _serverCertificate = _serverKeys.FirstOrDefault(x => x.PublicKeyHash == welcomeMessage.PublicKeyHash && x.Expiry > DateTimeOffset.Now);

                    if (_serverCertificate == null)
                    {
                        certificateRfreshHelper.Signal();
                        throw new ProtocolViolationException("No valid server certificate");
                    }

                    try
                    {
                        var tmp = RSA.Create();
                        tmp.ImportFromPem(_serverCertificate.PublicKey);
                        _serverPublicKey = tmp;
                    }
                    catch
                    {
                        certificateRfreshHelper.Signal();
                        throw new ProtocolViolationException("Invalid server certificate");
                    }

                    _state = ConnectionState.WelcomeReceived;
                    SendEnvelope(
                        welcomeEnvelope.RespondWith(
                            new AuthMessage(
                                _token,
                                ClientKey.ExportRSAPublicKeyPem(),
                                AutoUpdater.UpdaterManager.SelfVersion?.Version ?? "0.0.0",
                                PROTOCOL_VERSION
                            ),
                            "auth"
                        ),
                        force: true);
                    return;
                }

                if (_serverCertificate == null || _serverPublicKey == null || _serverCertificate.HasExpired())
                {
                    certificateRfreshHelper.Signal();
                    throw new ProtocolViolationException("No valid server certificate");
                }

                var envelope = TransportHelper.ParseFromEncryptedMessage(msg.Text, ClientKey);
                if (_state == ConnectionState.WelcomeReceived)
                {
                    if (envelope.GetMessageType() != MessageType.Auth)
                        throw new ProtocolViolationException("Expected welcome message");

                    var authMessage = envelope.GetPayload<AuthResultMessage>();
                    if (!authMessage.Accepted ?? false)
                        throw new ProtocolViolationException("Authentication failed");

                    _state = ConnectionState.Authenticated;

                    if ((authMessage.WillReplaceToken ?? false) && authMessage.NewToken != null)
                    {
                        _token = authMessage.NewToken;
                        await InvokeReKey();
                    }
                }
                else if (_state == ConnectionState.Authenticated)
                {
                    switch (envelope.GetMessageType())
                    {
                        case MessageType.Pong:
                            break;

                        case MessageType.Command:
                            await _onMessage(new CommandMessage(
                                envelope.GetPayload<CommandRequestMessage>(),
                                response => SendEnvelope(envelope.RespondWith(response))
                            ));
                            break;

                        default:
                            throw new ProtocolViolationException("Unexpected message");
                    }
                }
                else
                {
                    throw new ProtocolViolationException("Unexpected message");
                }


            }
            catch (Exception ex)
            {
                _state = ConnectionState.Error;
                Log.WriteMessage(LogMessageType.Error, LogTag, "WebsocketMessage", ex, "Failed to process message: {0}", msg);

                await _client.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Error");
                reconnectHelper.Signal();
            }
        });

        // Start the connection
        reconnectHelper.Signal();

        var t = await Task.WhenAny(
            heartbeatHelper.RunLoopAsync(),
            reconnectHelper.RunLoopAsync(),
            certificateRfreshHelper.RunLoopAsync()
        );

        _cancellationTokenSource.Cancel();

        // Re-throw any exceptions
        await t;
    }

    /// <summary>
    /// Helper method to invoke the rekey callback
    /// </summary>
    /// <returns>An awaitable task</returns>
    private Task InvokeReKey()
        => _onReKey(new ClaimedClientData(_token, _serverUrl, _certificateUrl, _serverKeys, null));

    /// <summary>
    /// Creates a new connection to the remote server
    /// </summary>
    /// <param name="serverUrl">The url to use</param>
    /// <param name="JWT">The JWT to use</param>
    /// <param name="certificateUrl">The certificate url to use</param>
    /// <param name="serverKeys">The server keys to use</param>
    /// <param name="cancellationToken">The token to cancel the connection</param>
    /// <param name="onReKey">The callback to call when rekeying</param>
    /// <param name="onMessage">The callback to call when a message is received</param>
    /// <returns></returns>
    public static Task Start(string serverUrl, string JWT, string certificateUrl, IEnumerable<MiniServerCertificate> serverKeys, CancellationToken cancellationToken, Func<ClaimedClientData, Task> onReKey, Func<CommandMessage, Task> onMessage)
        => Task.Run(async () =>
        {
            using var connection = new KeepRemoteConnection(serverUrl, JWT, certificateUrl, serverKeys, cancellationToken, onReKey, onMessage);
            await connection._runnerTask;
        });

    /// <summary>
    /// Gets the task representing the connection
    /// </summary>
    /// <returns>The task</returns>
    public Task Run()
        => _runnerTask;

    /// <summary>
    /// Stops the connection
    /// </summary>
    /// <returns>An awaitable task</returns>
    public Task Stop()
    {
        _cancellationTokenSource.Cancel();
        return _runnerTask;
    }

    /// <summary>
    /// Sends an enveloped message to the remote server
    /// </summary>
    /// <param name="envelope">The envelope to send</param>
    /// <returns>True if the message was sent</returns>
    private bool SendEnvelope(EnvelopedMessage envelope, bool force = true)
    {
        if ((_state != ConnectionState.Authenticated && !force) || _serverPublicKey == null)
            return false;

        _client.Send(TransportHelper.CreateEncryptedMessage(envelope with { From = ClientId }, _serverPublicKey));
        return true;
    }

    /// <summary>
    /// Sends a new command to the server
    /// </summary>
    /// <param name="message">The message to send</param>
    /// <returns>True if the message was sent</returns>
    public bool SendCommand(CommandRequestMessage message)
    {
        if (_state != ConnectionState.Authenticated || _serverPublicKey == null)
            return false;

        _client.Send(TransportHelper.CreateEncryptedMessage(new EnvelopedMessage()
        {
            From = ClientId,
            To = "server",
            Type = "command",
            MessageId = Guid.NewGuid().ToString(),
            Payload = JsonSerializer.Serialize(message, options: JsonOptions),
            ErrorMessage = null
        }, _serverPublicKey));

        return true;
    }

    /// <summary>
    /// The current state of the connection
    /// </summary>
    public ConnectionState State => _state;

    /// <summary>
    /// Creates a new connection to the remote server
    /// </summary>
    /// <param name="serverUrl">The url to use</param>
    /// <param name="JWT">The JWT token to use</param>
    /// 
    /// <param name="serverKeys">The server keys to use</param>
    /// <param name="onReKey">The callback to call when rekeying</param>
    /// <param name="onMessage">The callback to call when a message is received</param>
    /// <param name="cancellationToken">The token to cancel the connection</param>
    /// <returns>The connection object</returns>
    public static KeepRemoteConnection CreateRemoteListener(string serverUrl, string JWT, string certificateUrl, IEnumerable<MiniServerCertificate> serverKeys, CancellationToken cancellationToken, Func<ClaimedClientData, Task> onReKey, Func<CommandMessage, Task> onMessage)
        => new KeepRemoteConnection(serverUrl, JWT, certificateUrl, serverKeys, cancellationToken, onReKey, onMessage);

    /// <summary>
    /// Requests a certificate refresh
    /// </summary>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    private async Task RefreshCertificates(CancellationToken cancelToken)
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(_certificateUrl);
        if (response.IsSuccessStatusCode)
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancelToken);
            var serverKeys = await JsonSerializer.DeserializeAsync<IEnumerable<MiniServerCertificate>>(stream, options: RegisterForRemote.JsonOptions, cancellationToken: cancelToken);
            if (serverKeys != null && serverKeys.Any())
            {
                _serverKeys = serverKeys
                    .Where(x => !x.HasExpired() && !string.IsNullOrWhiteSpace(x.PublicKeyHash) && !string.IsNullOrWhiteSpace(x.PublicKey))
                    .ToList();

                await InvokeReKey();
            }
        }
    }

    /// </inheritdoc>
    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _client.Dispose();
        _cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// A wrapper for allowing external code to handle a command message
    /// </summary>
    public sealed class CommandMessage
    {
        /// <summary>
        /// The callback method that will receive the response
        /// </summary>
        private readonly Func<CommandResponseMessage, bool> _respondCommand;
        /// <summary>
        /// The command request message
        /// </summary>
        public CommandRequestMessage CommandRequestMessage { get; }

        /// <summary>
        /// Creates a new command message
        /// </summary>
        /// <param name="commandRequestMessage">The command request message</param>
        /// <param name="respondCommand">The callback method that will receive the response</param>
        public CommandMessage(CommandRequestMessage commandRequestMessage, Func<CommandResponseMessage, bool> respondCommand)
        {
            CommandRequestMessage = commandRequestMessage;
            _respondCommand = respondCommand;
        }

        /// <summary>
        /// Responds to the command message
        /// </summary>
        /// <param name="response">The response to send</param>
        /// <returns>True if the response was sent</returns>
        public bool Respond(CommandResponseMessage response)
            => _respondCommand(response);

        /// <summary>
        /// Handles the command message with a configured http client.
        /// The client must be configured with the correct base address and authorization headers.
        /// </summary>
        /// <param name="client">The pre-configured http client</param>
        /// <returns>An awaitable task</returns>
        public async Task Handle(HttpClient client)
        {
            try
            {
                var request = new HttpRequestMessage(new HttpMethod(CommandRequestMessage.Method), CommandRequestMessage.Path);
                if (!string.IsNullOrWhiteSpace(CommandRequestMessage.Body))
                    request.Content = new ByteArrayContent(Convert.FromBase64String(CommandRequestMessage.Body));
                if (CommandRequestMessage.Headers != null)
                {
                    foreach (var header in CommandRequestMessage.Headers)
                    {
                        if (header.Key == "Content-Type" && request.Content != null)
                            request.Content.Headers.ContentType = new MediaTypeHeaderValue(header.Value);
                        else
                            request.Headers.Add(header.Key, header.Value);
                    }
                }

                var response = await client.SendAsync(request);
                var responseBody = await response.Content.ReadAsByteArrayAsync();
                var responseHeaders = response.Headers.ToDictionary(x => x.Key, x => x.Value.First());

                Respond(new CommandResponseMessage((int)response.StatusCode, responseBody == null ? null : Convert.ToBase64String(responseBody), responseHeaders));
            }
            catch (Exception ex)
            {
                Respond(new CommandResponseMessage(500, ex.Message, null));
            }
        }
    }
}
