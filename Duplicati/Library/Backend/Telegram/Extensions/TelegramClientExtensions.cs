using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TLSharp.Core;
using TLSharp.Core.Network;

namespace Duplicati.Library.Backend.Extensions
{
    public static class TelegramClientExtensions
    {
        private static readonly BindingFlags _bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Default;

        public static async Task WrapperConnectAsync(this TelegramClient client, CancellationToken cancelToken = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            var oldTransportField = client.GetTcpTransport();
            var tcpClient = oldTransportField.GetTcpClient();
            var handler = client.GetTcpClientConnectionHandler();

            var remoteEndpoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
            var newTransport = new TcpTransport(remoteEndpoint.Address.ToString(), remoteEndpoint.Port, handler);

            client.GetTcpTransportFieldInfo().SetValue(client, newTransport);

            cancelToken.ThrowIfCancellationRequested();

            await client.ConnectAsync(false, cancelToken);

            cancelToken.ThrowIfCancellationRequested();

            oldTransportField.Dispose();
        }

        public static bool IsReallyConnected(this TelegramClient client)
        {
            if (client.IsConnected == false)
            {
                return false;
            }

            var sender = GetMtProtoSender(client);

            if (sender == null)
            {
                return false;
            }

            var tcpTransport = client.GetTcpTransport();

            if (tcpTransport == null || tcpTransport.IsConnected == false)
            {
                return false;
            }

            var tcpClient = tcpTransport.GetTcpClient();

            if (tcpClient == null || tcpClient.Connected == false)
            {
                return false;
            }

            return true;
        }

        private static TcpClientConnectionHandler GetTcpClientConnectionHandler(this TelegramClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            var handlerFieldInfo = typeof(TelegramClient).GetField("handler", _bindingFlags);
            var handler = (TcpClientConnectionHandler)handlerFieldInfo.GetValue(client);

            return handler;
        }

        private static TcpClient GetTcpClient(this TcpTransport tcpTransport)
        {
            if (tcpTransport == null)
            {
                throw new ArgumentNullException(nameof(tcpTransport));
            }

            var tcpClientFieldInfo = typeof(TcpTransport).GetField("tcpClient", _bindingFlags);
            var tcpClient = (TcpClient)tcpClientFieldInfo.GetValue(tcpTransport);

            return tcpClient;
        }

        private static FieldInfo GetTcpTransportFieldInfo(this TelegramClient client)
        {
            var transportFieldInfo = typeof(TelegramClient).GetField("transport", _bindingFlags);

            return transportFieldInfo;
        }

        private static TcpTransport GetTcpTransport(this TelegramClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            var transportFieldInfo = client.GetTcpTransportFieldInfo();
            var oldTransportField = (TcpTransport)transportFieldInfo.GetValue(client);

            return oldTransportField;
        }

        private static MtProtoSender GetMtProtoSender(this TelegramClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            var senderFieldInfo = typeof(TelegramClient).GetField("sender", _bindingFlags);
            var sender = (MtProtoSender)senderFieldInfo.GetValue(client);

            return sender;
        }
    }
}