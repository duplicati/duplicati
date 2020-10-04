using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TLSharp.Core;

namespace Duplicati.Library.Backend
{
    public class EncryptedFileSessionStore : ISessionStore
    {
        private static readonly object m_lockObj = new object();
        private static readonly uint[] m_lookup32 = CreateLookup32();

        private readonly string m_teleDataPath;
        private readonly string m_password;
        private readonly SHA256 m_sha = SHA256.Create();
        private static readonly ConcurrentDictionary<string, byte[]> m_userIdLastSessionMap = new ConcurrentDictionary<string, byte[]>();

        public EncryptedFileSessionStore(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentNullException(nameof(password));
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            m_teleDataPath = Path.Combine(appData, nameof(Duplicati), nameof(Telegram));
            m_password = password;

            Directory.CreateDirectory(m_teleDataPath);
        }


        public void Save(Session session)
        {
            var sessionId = session.SessionUserId;
            var filePath = GetSessionFilePath(sessionId);
            var sessionBytes = session.ToBytes();

            if (m_userIdLastSessionMap.TryGetValue(sessionId, out var sessionCache))
            {
                if (sessionCache.SequenceEqual(sessionBytes))
                {
                    return;
                }
            }

            WriteToEncryptedStorage(sessionBytes, m_password, filePath, sessionId);
        }

        public Session Load(string userId)
        {
            if (m_userIdLastSessionMap.TryGetValue(userId, out var cachedBytes))
            {
                var cachedSession = Session.FromBytes(cachedBytes, this, userId);
                return cachedSession;
            }

            var filePath = GetSessionFilePath(userId);
            var sessionBytes = ReadFromEncryptedStorage(m_password, filePath);

            if (sessionBytes == null)
            {
                return null;
            }

            var session = Session.FromBytes(sessionBytes, this, userId);
            return session;
        }

        private string GetSessionFilePath(string userId)
        {
            userId = userId.TrimStart('+');
            var sha = GetShortSha(userId);
            var sessionFilePath = Path.Combine(m_teleDataPath, $"t_{sha}.dat");

            return sessionFilePath;
        }

        private static void WriteToEncryptedStorage(byte[] bytesToWrite, string pass, string path, string sessionId)
        {
            lock (m_lockObj)
            {
                using (var sessionMs = new MemoryStream(bytesToWrite))
                using (var file = File.Open(path, FileMode.Create, FileAccess.Write))
                {
                    SharpAESCrypt.SharpAESCrypt.Encrypt(pass, sessionMs, file);
                }

                m_userIdLastSessionMap[sessionId] = bytesToWrite;
            }
        }

        private byte[] ReadFromEncryptedStorage(string pass, string path)
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Exists == false || fileInfo.Length == 0)
            {
                return null;
            }

            lock (m_lockObj)
            {
                using (var sessionMs = new MemoryStream())
                using (var file = File.Open(path, FileMode.Open, FileAccess.Read))
                {
                    SharpAESCrypt.SharpAESCrypt.Decrypt(pass, file, sessionMs);
                    return sessionMs.ToArray();
                }
            }
        }

        private string GetShortSha(string input)
        {
            var inputBytes = Encoding.UTF8.GetBytes(input);

            var longShaBytes = m_sha.ComputeHash(inputBytes);
            var longSha = ByteArrayToHexViaLookup32(longShaBytes);
            var result = longSha.Substring(0, 16);

            return result;
        }

        private static uint[] CreateLookup32()
        {
            var result = new uint[256];
            for (var i = 0; i < 256; i++)
            {
                var s = i.ToString("X2");
                result[i] = s[0] + ((uint)s[1] << 16);
            }

            return result;
        }

        private static string ByteArrayToHexViaLookup32(byte[] bytes)
        {
            var lookup32 = m_lookup32;
            var result = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var val = lookup32[bytes[i]];
                result[2 * i] = (char)val;
                result[2 * i + 1] = (char)(val >> 16);
            }

            return new string(result);
        }
    }
}