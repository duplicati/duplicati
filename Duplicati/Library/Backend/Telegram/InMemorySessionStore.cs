using System.Collections.Concurrent;
using System.Linq;
using TLSharp.Core;

namespace Duplicati.Library.Backend
{
    public class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<string, byte[]> m_phoneSessionBytesMap = new ConcurrentDictionary<string, byte[]>();
        private readonly ConcurrentDictionary<string, string> m_phonePhoneCodeHashMap = new ConcurrentDictionary<string, string>();

        private string[] _hexSessions => m_phoneSessionBytesMap.Values.Select(bytes => bytes.ToHex()).ToArray();

        public void Save(Session session)
        {
            m_phoneSessionBytesMap[session.SessionUserId] = session.ToBytes();
        }

        public Session Load(string phone)
        {
            if (m_phoneSessionBytesMap.TryGetValue(phone, out var sessionBytes))
            {
                return Session.FromBytes(sessionBytes, this, phone);
            }

            return null;
        }

        public void SetPhoneHash(string phone, string phoneCodeHash)
        {
            m_phonePhoneCodeHashMap[phone] = phoneCodeHash;
        }

        public string GetPhoneHash(string phone)
        {
            if (m_phonePhoneCodeHashMap.TryGetValue(phone, out var result))
            {
                return result;
            }

            return null;
        }
    }
}