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

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Duplicati.Library.Logging;

namespace Duplicati.Library.RemoteControl;

/// <summary>
/// Support class for keeping a connection to a remote server
/// </summary>
public class KeepRemoteConnection : IDisposable
{
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
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

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
    private static readonly string ClientId = string.IsNullOrWhiteSpace(AutoUpdater.UpdaterManager.MachineID)
        ? Guid.NewGuid().ToString()
        : AutoUpdater.UpdaterManager.MachineID;

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
        /// The connection is established, but not authenticated
        /// </summary>
        Connected,
        /// <summary>
        /// We received a welcome message
        /// </summary>
        WelcomeReceived,
        /// <summary>
        /// The connection is authenticated
        /// </summary>
        Authenticated
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
    /// The nonce challenge
    /// </summary>
    private string? _challenge;
    /// <summary>
    /// The task that runs the connection
    /// </summary>
    private Task _runnerTask;
    /// <summary>
    /// The currently negotiated server certificate
    /// </summary>
    private MiniServerCertificate? _serverCertificate;
    /// <summary>
    /// The time the certificate was last refreshed
    /// </summary>
    private DateTime _lastCertificateRefresh = DateTime.UnixEpoch;
    /// <summary>
    /// Task for requesting certificate refresh
    /// </summary>
    private TaskCompletionSource<bool> _refreshCertificates = new TaskCompletionSource<bool>();
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
    private Task RunMainLoop()
    {
        _client.ReconnectTimeout = ReconnectInterval;

        _client.ReconnectionHappened.Subscribe(info =>
        {
            _state = ConnectionState.Connected;
            Log.WriteMessage(LogMessageType.Information, LogTag, "WebsocketReconnect", "Reconnected to the server");
        });

        _client.DisconnectionHappened.Subscribe(info =>
        {
            _state = ConnectionState.NotConnected;
            _serverCertificate = null;
            Log.WriteMessage(LogMessageType.Warning, LogTag, "WebsocketDisconnect", "Disconnected from the server");
        });

        _client.MessageReceived.Subscribe(async msg =>
        {
            Log.WriteMessage(LogMessageType.Information, LogTag, "WebsocketMessage", "Received message from server: {0}", msg);

            try
            {
                var envelope = EnvelopedMessage.ForceParse(msg.Text);
                if (_serverCertificate == null || _state == ConnectionState.Connected)
                {
                    if (envelope.GetMessageType() != MessageType.Welcome)
                        throw new ProtocolViolationException("Expected welcome message");
                    if (string.IsNullOrWhiteSpace(envelope.PublicKeyHash))
                        throw new ProtocolViolationException("No public key hash in welcome message");
                    _serverCertificate = _serverKeys.FirstOrDefault(x => x.PublicKeyHash == envelope.PublicKeyHash && x.Expiry > DateTimeOffset.Now);

                    if (_serverCertificate == null)
                    {
                        _refreshCertificates.TrySetResult(true);
                        throw new ProtocolViolationException("No valid server certificate");
                    }
                }

                if (_serverCertificate == null)
                {
                    _refreshCertificates.TrySetResult(true);
                    throw new ProtocolViolationException("No valid server certificate");
                }

                envelope.ValidateSignature(_serverCertificate?.PublicKey);

                if (_state == ConnectionState.Connected)
                {
                    // TODO: The message could be a replay attack
                    if (envelope.GetMessageType() != MessageType.Welcome)
                        throw new ProtocolViolationException("Expected welcome message");

                    _state = ConnectionState.WelcomeReceived;
                    Log.WriteMessage(LogMessageType.Information, LogTag, "WebsocketAuthenticated", "Connected with the server");

                    _challenge = RandomNumberGenerator.GetHexString(64);
                    SendEnvelope(envelope.RespondWith(new AuthMessage(_token, _challenge)));
                }
                else if (_state == ConnectionState.WelcomeReceived)
                {
                    if (envelope.GetMessageType() != MessageType.Auth)
                        throw new ProtocolViolationException("Expected welcome message");

                    var authMessage = envelope.GetPayload<AuthResultMessage>();
                    if (!authMessage.Accepted ?? false)
                        throw new ProtocolViolationException("Authentication failed");

                    if (authMessage.SignedChallenge == null)
                        throw new ProtocolViolationException("Invalid Json message");

                    using RSA rsa = RSA.Create();
                    rsa.ImportFromPem(_serverCertificate?.PublicKey);
                    if (!rsa.VerifyData(Encoding.UTF8.GetBytes(_challenge!), Convert.FromHexString(authMessage.SignedChallenge), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                        throw new EnvelopeJsonParsingException("Invalid Json message");

                    if ((authMessage.WillReplaceToken ?? false) && authMessage.NewToken != null)
                    {
                        _token = authMessage.NewToken;
                        await InvokeReKey();
                    }

                    _state = ConnectionState.Authenticated;
                }
                else if (_state == ConnectionState.Authenticated)
                {
                    switch (envelope.GetMessageType())
                    {
                        case MessageType.Pong:
                            break;

                        case MessageType.Command:
                            await _onMessage(new CommandMessage(envelope.GetPayload<CommandRequestMessage>(), response =>
                            {
                                SendEnvelope(envelope.RespondWith(response));
                                return true;
                            }));
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
                Log.WriteMessage(LogMessageType.Error, LogTag, "WebsocketMessage", ex, "Failed to process message: {0}", msg);
                // TODO: This leaks if we keep getting exceptions
                _client.Reconnect();
            }
        });

        return Task.WhenAny(
            _client.Start(),
            RunHeartbeatLoop(),
            RunCertificateRefreshLoop()
        );
    }

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
    private bool SendEnvelope(EnvelopedMessage envelope)
    {
        if (_state != ConnectionState.Authenticated)
            return false;

        _client.Send((envelope with { From = ClientId }).WithSignature(ClientKey).ToJson());
        return true;
    }

    /// <summary>
    /// Sends a new command to the server
    /// </summary>
    /// <param name="message">The message to send</param>
    /// <returns>True if the message was sent</returns>
    public bool SendCommand(CommandRequestMessage message)
    {
        if (_state != ConnectionState.Authenticated)
            return false;

        _client.Send(new EnvelopedMessage()
        {
            From = ClientId,
            To = "server",
            Type = "command",
            MessageId = Guid.NewGuid().ToString()
        }
        .WithPayload(message)
        .WithSignature(ClientKey).ToJson());

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
    /// Sends a heartbeat message to the server
    /// </summary>
    /// <param name="client">The client to send the message with</param>
    /// <param name="cancellationToken">The token to cancel the heartbeat</param>
    /// <returns>An awaitable task</returns>
    private async Task RunHeartbeatLoop()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            await Task.Delay(HeartbeatInterval, _cancellationTokenSource.Token);
            SendEnvelope(new EnvelopedMessage()
            {
                From = ClientId,
                To = "server",
                Type = "ping",
                MessageId = Guid.NewGuid().ToString()
            });
        }
    }

    /// <summary>
    /// Runs a loop that refreshes the server certificates
    /// </summary>
    /// <returns>An awaitable task</returns>
    private async Task RunCertificateRefreshLoop()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            var t = await Task.WhenAny(_refreshCertificates.Task, Task.Delay(CertificateRefreshInterval, _cancellationTokenSource.Token));
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                return;

            if (t == _refreshCertificates.Task)
                Interlocked.Exchange(ref _refreshCertificates, new TaskCompletionSource<bool>());

            if (_lastCertificateRefresh.AddMinutes(5) < DateTime.Now)
            {
                using var client = new HttpClient();
                var response = await client.GetAsync(_certificateUrl);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(_cancellationTokenSource.Token);
                    var serverKeys = await JsonSerializer.DeserializeAsync<IEnumerable<MiniServerCertificate>>(stream, cancellationToken: _cancellationTokenSource.Token);
                    if (serverKeys != null && serverKeys.Any())
                    {
                        _lastCertificateRefresh = DateTime.Now;
                        _serverKeys = serverKeys;
                        await InvokeReKey();
                    }
                }
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
            var request = new HttpRequestMessage(new HttpMethod(CommandRequestMessage.Method), CommandRequestMessage.Path);
            if (CommandRequestMessage.Body != null)
                request.Content = new ByteArrayContent(CommandRequestMessage.Body);
            if (CommandRequestMessage.Headers != null)
                foreach (var header in CommandRequestMessage.Headers)
                    request.Headers.Add(header.Key, header.Value);

            var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsByteArrayAsync();
            var responseHeaders = response.Headers.ToDictionary(x => x.Key, x => x.Value.First());

            Respond(new CommandResponseMessage((int)response.StatusCode, responseBody, responseHeaders));
        }
    }
}
