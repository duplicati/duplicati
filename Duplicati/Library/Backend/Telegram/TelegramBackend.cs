﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using TeleSharp.TL;
using TeleSharp.TL.Channels;
using TeleSharp.TL.Messages;
using TLSharp.Core;
using TLSharp.Core.Exceptions;
using TLSharp.Core.Network.Exceptions;
using TLSharp.Core.Utils;
using TLRequestDeleteMessages = TeleSharp.TL.Channels.TLRequestDeleteMessages;

namespace Duplicati.Library.Backend
{
    public class Telegram : IBackend
    {
        private const int MEBIBYTE_IN_BYTES = 1048576;
        private static readonly string LOG_TAG = Log.LogTagFromType(typeof(Telegram));

        private static readonly InMemorySessionStore m_sessionStore = new InMemorySessionStore();
        private static readonly object m_lockObj = new object();
        private readonly int m_apiId;
        private readonly string m_apiHash;
        private readonly string m_authCode;
        private readonly string m_password;
        private readonly string m_channelName;
        private readonly string m_phoneNumber;
        private TLChannel m_channelCache;
        private static TelegramClient m_telegramClient;

        public Telegram()
        { }

        public Telegram(string url, Dictionary<string, string> options)
        {
            if (options.TryGetValue(Strings.API_ID_KEY, out var apiId))
            {
                m_apiId = int.Parse(apiId);
            }

            if (options.TryGetValue(Strings.API_HASH_KEY, out var apiHash))
            {
                m_apiHash = apiHash.Trim();
            }

            if (options.TryGetValue(Strings.PHONE_NUMBER_KEY, out var phoneNumber))
            {
                m_phoneNumber = phoneNumber.Trim();
            }

            if (options.TryGetValue(Strings.AUTH_CODE_KEY, out var authCode))
            {
                m_authCode = authCode.Trim();
            }

            if (options.TryGetValue(Strings.AUTH_PASSWORD, out var password))
            {
                m_password = password.Trim();
            }

            if (options.TryGetValue(Strings.CHANNEL_NAME, out var channelName))
            {
                m_channelName = channelName.Trim();
            }

            if (m_apiId == 0)
            {
                throw new UserInformationException(Strings.NoApiIdError, nameof(Strings.NoApiIdError));
            }

            if (string.IsNullOrEmpty(m_apiHash))
            {
                throw new UserInformationException(Strings.NoApiHashError, nameof(Strings.NoApiHashError));
            }

            if (string.IsNullOrEmpty(m_phoneNumber))
            {
                throw new UserInformationException(Strings.NoPhoneNumberError, nameof(Strings.NoPhoneNumberError));
            }

            if (string.IsNullOrEmpty(m_channelName))
            {
                throw new UserInformationException(Strings.NoChannelNameError, nameof(Strings.NoChannelNameError));
            }

            InitializeTelegramClient(m_apiId, m_apiHash, m_phoneNumber);
        }

        private static void InitializeTelegramClient(int apiId, string apiHash, string phoneNumber)
        {
            var tmpTelegramClient = m_telegramClient;
            m_telegramClient = new TelegramClient(apiId, apiHash, m_sessionStore, phoneNumber);
            tmpTelegramClient?.Dispose();
        }

        public void Dispose()
        {
            // Do not dispose m_telegramClient because of "session-based" authentication.
        }

        public string DisplayName { get; } = Strings.DisplayName;
        public string ProtocolKey { get; } = "https";

        public IEnumerable<IFileEntry> List()
        {
            return SafeExecute<IEnumerable<IFileEntry>>(() =>
                {
                    Authenticate();
                    EnsureChannelCreated();
                    var fileInfos = ListChannelFileInfos();
                    var result = fileInfos.Select(fi => fi.ToFileEntry());
                    return result;
                },
                nameof(List));
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            return SafeExecute(() =>
                {
                    var channel = GetChannel();
                    using (var fs = File.OpenRead(filename))
                    {
                        var fsReader = new StreamReader(fs);

                        cancelToken.ThrowIfCancellationRequested();
                        EnsureConnected();
                        var file = m_telegramClient.UploadFile(remotename, fsReader, cancelToken).GetAwaiter().GetResult();

                        cancelToken.ThrowIfCancellationRequested();
                        var inputPeerChannel = new TLInputPeerChannel {ChannelId = channel.Id, AccessHash = (long)channel.AccessHash};
                        var fileNameAttribute = new TLDocumentAttributeFilename
                        {
                            FileName = remotename
                        };

                        EnsureConnected();
                        m_telegramClient.SendUploadedDocument(inputPeerChannel, file, remotename, "application/zip", new TLVector<TLAbsDocumentAttribute> {fileNameAttribute}, cancelToken).GetAwaiter().GetResult();
                        fs.Close();
                    }

                    return Task.CompletedTask;
                },
                $"{nameof(PutAsync)}({remotename})");
        }

        public void Get(string remotename, string filename)
        {
            SafeExecute(() =>
                {
                    var fileInfo = ListChannelFileInfos().First(fi => fi.Name == remotename);
                    var fileLocation = fileInfo.ToFileLocation();

                    var limit = MEBIBYTE_IN_BYTES;
                    var currentOffset = 0;
                    
                    using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
                    {
                        while (currentOffset < fileInfo.Size)
                        {
                            try
                            {
                                EnsureConnected();
                                var file = m_telegramClient.GetFile(fileLocation, limit, currentOffset).GetAwaiter().GetResult();
                                fs.Write(file.Bytes, 0, file.Bytes.Length);
                                currentOffset += file.Bytes.Length;
                            }
                            catch (InvalidOperationException e)
                            {
                                if (e.Message.Contains("Couldn't read the packet length") == false)
                                {
                                    throw;
                                }
                            }
                        }

                        fs.Close();
                    }
                },
                $"{nameof(Get)}({remotename})");
        }

        public void Delete(string remotename)
        {
            SafeExecute(() =>
                {
                    var channel = GetChannel();
                    var fileInfo = ListChannelFileInfos().FirstOrDefault(fi => fi.Name == remotename);
                    if (fileInfo == null)
                    {
                        return;
                    }

                    var request = new TLRequestDeleteMessages
                    {
                        Channel = new TLInputChannel
                        {
                            ChannelId = channel.Id,
                            AccessHash = channel.AccessHash.Value
                        },
                        Id = new TLVector<int> {fileInfo.MessageId}
                    };

                    EnsureConnected();
                    m_telegramClient.SendRequestAsync<TLAffectedMessages>(request).GetAwaiter().GetResult();
                },
                $"{nameof(Delete)}({remotename})");
        }

        public List<ChannelFileInfo> ListChannelFileInfos()
        {
            var channel = GetChannel();

            var inputPeerChannel = new TLInputPeerChannel {ChannelId = channel.Id, AccessHash = channel.AccessHash.Value};
            var result = new List<ChannelFileInfo>();
            var cMinId = (int?)0;

            while (cMinId != null)
            {
                var newCMinId = RetrieveMessages(inputPeerChannel, result, cMinId.Value);
                if (newCMinId == cMinId)
                {
                    break;
                }

                cMinId = newCMinId;
            }

            result = result.Distinct().ToList();
            return result;
        }

        private int? RetrieveMessages(TLInputPeerChannel inputPeerChannel, List<ChannelFileInfo> result, int maxDate)
        {
            EnsureConnected();
            var absHistory = m_telegramClient.GetHistoryAsync(inputPeerChannel, offsetDate: maxDate).GetAwaiter().GetResult();
            var history = ((TLChannelMessages)absHistory).Messages.OfType<TLMessage>();
            var minDate = (int?)null;

            foreach (var msg in history)
            {
                minDate = minDate == null || minDate > msg.Id ? msg.Date : minDate;

                if (msg.Media is TLMessageMediaDocument media)
                {
                    var mediaDoc = media.Document as TLDocument;
                    var fileInfo = new ChannelFileInfo(msg.Id, mediaDoc.AccessHash, mediaDoc.Id, mediaDoc.Version, mediaDoc.Size, media.Caption, DateTimeOffset.FromUnixTimeSeconds(msg.Date).UtcDateTime);

                    result.Add(fileInfo);
                }
            }

            return minDate;
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
            // No need to use our method
            lock (m_lockObj)
            {
                Authenticate();
            }
        }

        public void CreateFolder()
        {
            SafeExecute(() =>
                {
                    Authenticate();
                    EnsureChannelCreated();
                },
                nameof(CreateFolder));
        }

        private TLChannel GetChannel()
        {
            if (m_channelCache != null)
            {
                return m_channelCache;
            }

            EnsureConnected();
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

            var channel = (TLChannel)absChats.FirstOrDefault(chat => chat is TLChannel tlChannel && tlChannel.Title == m_channelName);
            m_channelCache = channel;
            return channel;
        }

        private void EnsureChannelCreated()
        {
            var channel = GetChannel();
            if (channel == null)
            {
                var newGroup = new TLRequestCreateChannel
                {
                    Broadcast = false,
                    Megagroup = false,
                    Title = m_channelName,
                    About = string.Empty
                };

                EnsureConnected();
                m_telegramClient.SendRequestAsync<object>(newGroup).GetAwaiter().GetResult();
            }
        }

        private void Authenticate()
        {
            EnsureConnected();

            if (IsAuthenticated())
            {
                return;
            }

            try
            {
                var phoneCodeHash = m_sessionStore.GetPhoneHash(m_phoneNumber);
                if (phoneCodeHash == null)
                {
                    EnsureConnected();
                    phoneCodeHash = m_telegramClient.SendCodeRequestAsync(m_phoneNumber).GetAwaiter().GetResult();
                    m_sessionStore.SetPhoneHash(m_phoneNumber, phoneCodeHash);
                    m_telegramClient.Session.Save();

                    throw new UserInformationException(Strings.NoOrWrongAuthCodeError, nameof(Strings.NoOrWrongAuthCodeError));
                }

                m_telegramClient.MakeAuthAsync(m_phoneNumber, phoneCodeHash, m_authCode).GetAwaiter().GetResult();
            }
            catch (CloudPasswordNeededException)
            {
                if (string.IsNullOrEmpty(m_password))
                {
                    m_telegramClient.Session.Save();
                    throw new UserInformationException(Strings.NoPasswordError, nameof(Strings.NoPasswordError));
                }

                EnsureConnected();
                var passwordSetting = m_telegramClient.GetPasswordSetting().GetAwaiter().GetResult();
                m_telegramClient.MakeAuthWithPasswordAsync(passwordSetting, m_password).GetAwaiter().GetResult();
            }

            m_telegramClient.Session.Save();
        }

        private void EnsureConnected()
        {
            var isConnected = false;

            while (isConnected == false)
            {
                try
                {
                    isConnected = m_telegramClient.ConnectAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (FloodException floodExc)
                {
                    var randSeconds = new Random().Next(0, 15);
                    Log.WriteInformationMessage(LOG_TAG, nameof(Strings.TELEGRAM_FLOOD), Strings.TELEGRAM_FLOOD, floodExc.TimeToWait.TotalSeconds + randSeconds);
                    Thread.Sleep(floodExc.TimeToWait + TimeSpan.FromSeconds(randSeconds));
                }
                catch (Exception e)
                {
                    InitializeTelegramClient(m_apiId, m_apiHash, m_phoneNumber);
                    isConnected = false;
                }
            }
        }

        private bool IsAuthenticated()
        {
            var isAuthorized = m_telegramClient.IsUserAuthorized();
            return isAuthorized;
        }

        private void SafeExecute(Action action, string actionName)
        {
            lock (m_lockObj)
            {
                Log.WriteInformationMessage(LOG_TAG, nameof(Strings.STARTING_EXECUTING), Strings.STARTING_EXECUTING, actionName);
                try
                {
                    action();
                }
                catch (UserInformationException uiExc)
                {
                    Log.WriteWarningMessage(LOG_TAG, nameof(Strings.USER_INFO_EXC), uiExc, Strings.USER_INFO_EXC);
                    throw;
                }
                catch (FloodException floodExc)
                {
                    var randSeconds = new Random().Next(0, 15);
                    Log.WriteInformationMessage(LOG_TAG, nameof(Strings.TELEGRAM_FLOOD), Strings.TELEGRAM_FLOOD, floodExc.TimeToWait.TotalSeconds + randSeconds);
                    Thread.Sleep(floodExc.TimeToWait + TimeSpan.FromSeconds(randSeconds));
                    SafeExecute(action, actionName);
                }
                catch (Exception e)
                {
                    Log.WriteErrorMessage(LOG_TAG, nameof(Strings.EXCEPTION_RETRY), e, Strings.EXCEPTION_RETRY);
                    action();
                }
            }

            Log.WriteInformationMessage(LOG_TAG, nameof(Strings.DONE_EXECUTING), Strings.DONE_EXECUTING, actionName);
        }

        private T SafeExecute<T>(Func<T> func, string actionName)
        {
            lock (m_lockObj)
            {
                Log.WriteInformationMessage(LOG_TAG, nameof(Strings.STARTING_EXECUTING), Strings.STARTING_EXECUTING, actionName);
                try
                {
                    var res = func();
                    Log.WriteInformationMessage(LOG_TAG, nameof(Strings.DONE_EXECUTING), Strings.DONE_EXECUTING, actionName);
                    return res;
                }
                catch (UserInformationException uiExc)
                {
                    Log.WriteWarningMessage(LOG_TAG, nameof(Strings.USER_INFO_EXC), uiExc, Strings.USER_INFO_EXC);
                    throw;
                }
                catch (FloodException floodExc)
                {
                    var randSeconds = new Random().Next(0, 15);
                    Log.WriteInformationMessage(LOG_TAG, nameof(Strings.TELEGRAM_FLOOD), Strings.TELEGRAM_FLOOD, floodExc.TimeToWait.TotalSeconds + randSeconds);
                    Thread.Sleep(floodExc.TimeToWait);
                    var res = SafeExecute(func, actionName);
                    Log.WriteInformationMessage(LOG_TAG, nameof(Strings.DONE_EXECUTING), Strings.DONE_EXECUTING, actionName);
                    return res;
                }
                catch (Exception e)
                {
                    Log.WriteErrorMessage(LOG_TAG, nameof(Strings.EXCEPTION_RETRY), e, Strings.EXCEPTION_RETRY);
                    return func();
                }
            }
        }
    }
}