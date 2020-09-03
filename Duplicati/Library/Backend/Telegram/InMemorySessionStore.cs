using System.Collections.Concurrent;
using TLSharp.Core;

namespace Duplicati.Library.Backend
{
    public class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<string, byte[]> m_phoneSessionBytesMap = new ConcurrentDictionary<string, byte[]>();
        private readonly ConcurrentDictionary<string, string> m_phonePhoneCodeHashMap = new ConcurrentDictionary<string, string>();

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
            if (phoneCodeHash == null)
            {
                m_phoneSessionBytesMap.TryRemove(phone, out _);
                return;
            }
            
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

        public Session GetSession(string phone)
        {
            if (m_phoneSessionBytesMap.TryGetValue(phone, out var sessionBytes))
            {
                return Session.FromBytes(sessionBytes, this, phone);
            }

            return null;
        }
    }
}