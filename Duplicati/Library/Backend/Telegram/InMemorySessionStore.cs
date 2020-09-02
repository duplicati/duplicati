using System.Collections.Concurrent;
using TLSharp.Core;

namespace Duplicati.Library.Backend
{
    public class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<string, byte[]> m_userIdSessionBytesMap = new ConcurrentDictionary<string, byte[]>();
        private readonly ConcurrentDictionary<string, string> m_userIdPhoneCodeHashMap = new ConcurrentDictionary<string, string>();

        public void Save(Session session)
        {
            m_userIdSessionBytesMap[session.SessionUserId] = session.ToBytes();
        }

        public Session Load(string sessionUserId)
        {
            if (m_userIdSessionBytesMap.TryGetValue(sessionUserId, out var sessionBytes))
            {
                return Session.FromBytes(sessionBytes, this, sessionUserId);
            }

            return null;
        }

        public void SetPhoneHash(string sessionUserId, string phoneCodeHash)
        {
            m_userIdPhoneCodeHashMap[sessionUserId] = phoneCodeHash;
        }

        public string GetPhoneHash(string sessionUserId)
        {
            if (m_userIdPhoneCodeHashMap.TryGetValue(sessionUserId, out var result))
            {
                return result;
            }

            return null;
        }
    }
}