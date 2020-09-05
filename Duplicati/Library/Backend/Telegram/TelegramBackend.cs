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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using TeleSharp.TL;
using TeleSharp.TL.Channels;
using TeleSharp.TL.Messages;
using TLSharp.Core;
using TLSharp.Core.Exceptions;
using TLSharp.Core.Utils;

namespace Duplicati.Library.Backend
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class Telegram : IBackend
    {
        private static readonly InMemorySessionStore m_sessionStore = new InMemorySessionStore();

        private readonly int m_apiId;
        private readonly string m_apiHash;
        private readonly string m_authCode;
        private readonly string m_password;
        private readonly string m_channelName;
        private readonly string m_phoneNumber;
        private readonly TelegramClient m_telegramClient;

        public Telegram()
        { }

        public Telegram(string url, Dictionary<string, string> options)
        {
            if (options.TryGetValue(Strings.API_ID_KEY, out var apiId))
                m_apiId = int.Parse(apiId);

            if (options.TryGetValue(Strings.API_HASH_KEY, out var apiHash))
                m_apiHash = apiHash.Trim();

            if (options.TryGetValue(Strings.PHONE_NUMBER_KEY, out var phoneNumber))
                m_phoneNumber = phoneNumber.Trim();

            if (options.TryGetValue(Strings.AUTH_CODE_KEY, out var authCode))
                m_authCode = authCode.Trim();

            if (options.TryGetValue(Strings.AUTH_PASSWORD, out var password))
                m_password = password.Trim();

            if (options.TryGetValue(Strings.CHANNEL_NAME, out var channelName))
                m_channelName = channelName.Trim();

            if (m_apiId == 0)
                throw new UserInformationException(Strings.NoApiIdError, nameof(Strings.NoApiIdError));

            if (string.IsNullOrEmpty(m_apiHash))
                throw new UserInformationException(Strings.NoApiHashError, nameof(Strings.NoApiHashError));

            if (string.IsNullOrEmpty(m_phoneNumber))
                throw new UserInformationException(Strings.NoPhoneNumberError, nameof(Strings.NoPhoneNumberError));

            if (string.IsNullOrEmpty(m_channelName))
                throw new UserInformationException(Strings.NoChannelNameError, nameof(Strings.NoChannelNameError));

            m_telegramClient = new TelegramClient(m_apiId, m_apiHash, m_sessionStore, m_phoneNumber);
        }

        public void Dispose()
        { }

        public string DisplayName { get; } = "Telegram";
        public string ProtocolKey { get; } = "https";

        public IEnumerable<IFileEntry> List()
        {
            AuthenticateAsync().GetAwaiter().GetResult();
            EnsureChannelCreatedAsync().GetAwaiter();
            
            var result = new List<FileEntry>();
            var channel = GetChannel();
            if (channel == null)
            {
                throw new UserInformationException(Strings.CouldNotCreateChannelError, nameof(Strings.CouldNotCreateChannelError));
            }
            
            var inputPeerChannel = new TLInputPeerChannel { ChannelId = channel.Id, AccessHash = channel.AccessHash.Value};
            var absHistory = m_telegramClient.GetHistoryAsync(inputPeerChannel, 0, -1, 0, Int32.MaxValue).GetAwaiter().GetResult();
            var history = ((TLChannelMessages)absHistory).Messages
                .Where(msg => msg is TLMessage tlMsg && tlMsg.Media is TLMessageMediaDocument)
                .Select(msg => (TLMessageMediaDocument)((TLMessage)msg).Media);
            
            foreach (var docFile in history)
            {
                var typedDoc = (TLDocument)docFile.Document;
                var fileEntry = new FileEntry(docFile.Caption, typedDoc.Size);
                result.Add(fileEntry);
            }
            
            return result;
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            await AuthenticateAsync();
            
            var channel = GetChannel();
            var fs = File.OpenRead(filename);
            var fsReader = new StreamReader(fs);
            cancelToken.ThrowIfCancellationRequested();
            var file = await m_telegramClient.UploadFile(filename, fsReader, cancelToken);
            cancelToken.ThrowIfCancellationRequested();
            var inputPeerChannel = new TLInputPeerChannel { ChannelId = channel.Id, AccessHash = (long)channel.AccessHash};
            await m_telegramClient.SendUploadedDocument(inputPeerChannel, file, filename, "binary", new TLVector<TLAbsDocumentAttribute>(), cancelToken);
        }

        public void Get(string remotename, string filename)
        {
            AuthenticateAsync().GetAwaiter().GetResult();
            return;
            var fs = File.OpenWrite(filename);
            
        }

        public void Delete(string remotename)
        {
            AuthenticateAsync().GetAwaiter().GetResult();
        }


        public IList<ICommandLineArgument> SupportedCommands { get; } = new List<ICommandLineArgument>
        {
            new CommandLineArgument(Strings.API_ID_KEY, CommandLineArgument.ArgumentType.Integer, Strings.ApiIdShort, Strings.ApiIdLong),
            new CommandLineArgument(Strings.API_HASH_KEY, CommandLineArgument.ArgumentType.String, Strings.ApiHashShort, Strings.ApiHashLong),
            new CommandLineArgument(Strings.PHONE_NUMBER_KEY, CommandLineArgument.ArgumentType.String, Strings.PhoneNumberShort, Strings.PhoneNumberLong),
            new CommandLineArgument(Strings.AUTH_CODE_KEY, CommandLineArgument.ArgumentType.String, Strings.AuthCodeShort, Strings.AuthCodeLong),
            new CommandLineArgument(Strings.AUTH_PASSWORD, CommandLineArgument.ArgumentType.String, Strings.PasswordShort, Strings.PasswordLong),
            new CommandLineArgument(Strings.CHANNEL_NAME, CommandLineArgument.ArgumentType.String, Strings.ChannelNameShort, Strings.ChannelNameLong)
        };

        public string Description { get; } = Strings.Description;

        public string[] DNSName { get; }

        public void Test()
        {
            AuthenticateAsync().GetAwaiter().GetResult();
        }

        public void CreateFolder()
        {
            AuthenticateAsync().GetAwaiter().GetResult();
            EnsureChannelCreatedAsync().GetAwaiter().GetResult();
        }

        private TLChannel GetChannel()
        {
            var absDialogs = m_telegramClient.GetUserDialogsAsync().GetAwaiter().GetResult();
            var userDialogs = absDialogs as TLDialogs;
            var userDialogsSlice = absDialogs as TLDialogsSlice;
            TLVector<TLAbsChat> absChats; 

            if (userDialogs != null)
            {
                absChats = userDialogs.Chats;
            }
            else
            {
                absChats = userDialogsSlice.Chats;
            }
            
            var channel = (TLChannel)absChats.FirstOrDefault(chat =>
                chat is TLChannel tlChannel && tlChannel.Title == m_channelName);

            return channel;
        }

        private async Task EnsureChannelCreatedAsync()
        {
            var channel = GetChannel();
            if (channel == null)
            {
                var newGroup = new TLRequestCreateChannel
                {
                    Broadcast = false,
                    Megagroup = false,
                    Title = m_channelName
                };
                await m_telegramClient.SendRequestAsync<object>(newGroup);
            }
        }

        private async Task AuthenticateAsync()
        {
            await EnsureConnectedAsync();
            
            if (IsAuthenticated())
            {
                return;
            }

            try
            {
                var phoneCodeHash = m_sessionStore.GetPhoneHash(m_phoneNumber);
                if (phoneCodeHash == null)
                {
                    phoneCodeHash = await m_telegramClient.SendCodeRequestAsync(m_phoneNumber);
                    m_sessionStore.SetPhoneHash(m_phoneNumber, phoneCodeHash);
                    m_telegramClient.Session.Save();

                    throw new UserInformationException(Strings.NoAuthCodeError, nameof(Strings.NoAuthCodeError));
                }

                await m_telegramClient.MakeAuthAsync(m_phoneNumber, phoneCodeHash, m_authCode);
            }
            catch (CloudPasswordNeededException)
            {
                if (string.IsNullOrEmpty(m_password))
                {
                    m_telegramClient.Session.Save();
                    throw new UserInformationException(Strings.NoPasswordError, nameof(Strings.NoPasswordError));
                }

                var passwordSetting = await m_telegramClient.GetPasswordSetting();
                await m_telegramClient.MakeAuthWithPasswordAsync(passwordSetting, m_password);
            }
            
            m_telegramClient.Session.Save();
        }

        private async Task EnsureConnectedAsync()
        {
            try
            {
                await m_telegramClient.ConnectAsync();
            }
            catch
            { }
        }

        private bool IsAuthenticated()
        {
            var isAuthorized = m_telegramClient.IsUserAuthorized();
            return isAuthorized;
        }
    }
}
