using System.Net.Sockets;
using System.Reflection;
using TLSharp.Core;
using TLSharp.Core.Network;

namespace Duplicati.Library.Backend.Extensions
{
    public static class TelegramClientExtensions
    {
        private static BindingFlags _bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Default;

        public static bool IsReallyConnected(this TelegramClient client)
        {
            if (client.IsConnected == false)
            {
                return false;
            }

            var senderFieldInfo = typeof(TelegramClient).GetField("sender", _bindingFlags);
            var sender = (MtProtoSender)senderFieldInfo.GetValue(client);

            if (sender == null)
            {
                return false;
            }

            var transportFieldInfo = typeof(TelegramClient).GetField("transport", _bindingFlags);
            var transportField = (TcpTransport)transportFieldInfo.GetValue(client);

            if (transportField == null || transportField.IsConnected == false)
            {
                return false;
            }

            var tcpClientFieldInfo = typeof(TcpTransport).GetField("tcpClient", _bindingFlags);
            var tcpClient = (TcpClient)tcpClientFieldInfo.GetValue(transportField);

            if (tcpClient == null || tcpClient.Connected == false)
            {
                return false;
            }
            
            return true;
        }
    }
}