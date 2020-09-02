#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion

using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TLSharp.Core;

namespace Duplicati.Library.Backend
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class Telegram : IBackend
    {
        private static readonly InMemorySessionStore m_sessionStore = new InMemorySessionStore();
        
        private readonly int m_apiId;
        private readonly string m_apiHash;
        private readonly string m_phoneNumber;
        
        private string m_authCode;
        private string m_password;

        public Telegram()
        {
            Console.WriteLine("Welcome to Telegram parameterless");
        }

        // ReSharper disable once UnusedParameter.Local
        // ToDo remove url if not needed
        public Telegram(string url, Dictionary<string, string> options)
        {
            Console.WriteLine("Welcome to Telegram");
            if (options.TryGetValue("api_id", out var apiId))
                m_apiId = int.Parse(apiId);
            
            if (options.TryGetValue("api_hash", out var apiHash))
                m_apiHash = apiHash;
            
            if (options.TryGetValue("phone_number", out var phoneNumber))
                m_phoneNumber = phoneNumber;

            if (options.TryGetValue("auth_code", out var authCode))
                m_authCode = authCode;
            
            if (options.TryGetValue("password", out var password))
                m_password = password;

            if (m_apiId == 0)
                throw new UserInformationException(Strings.TelegramBackend.NoApiIdError, "NoApiIdError");
            
            if (string.IsNullOrEmpty(m_apiHash))
                throw new UserInformationException(Strings.TelegramBackend.NoApiHashError, "NoApiHashError");

            if (string.IsNullOrEmpty(m_phoneNumber))
                throw new UserInformationException(Strings.TelegramBackend.NoPhoneNumberError, "NoPhoneNumberError");
        }

        public void Dispose()
        { }

        public string DisplayName { get; } = "Telegram";
        public string ProtocolKey { get; } = "https";
        
        public IEnumerable<IFileEntry> List()
        {
            throw new NotImplementedException();
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            throw new NotImplementedException();
        }

        public void Get(string remotename, string filename)
        {
            throw new NotImplementedException();
        }

        public void Delete(string remotename)
        {
            throw new NotImplementedException();
        }

        public IList<ICommandLineArgument> SupportedCommands { get; }

        public string Description { get; } = Strings.TelegramBackend.Description;
        
        public string[] DNSName { get; }
        
        public void Test()
        {
            throw new NotImplementedException();
        }

        public void CreateFolder()
        {
            throw new NotImplementedException();
        }
    }
}
