using System;
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
    public class Telegram : IStreamingBackend, IBackend
    {
        private const int MEBIBYTE_IN_BYTES = 1048576;

        private static readonly InMemorySessionStore m_sessionStore = new InMemorySessionStore();
        private static readonly string m_logTag = Log.LogTagFromType(typeof(Telegram));
        private static readonly object m_lockObj = new object();
        private static TelegramClient m_telegramClient;

        private readonly int m_apiId;
        private readonly string m_apiHash;
        private readonly string m_authCode;
        private readonly string m_password;
        private readonly string m_channelName;
        private readonly string m_phoneNumber;
        private TLChannel m_channelCache;

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

        public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            SafeExecute(() =>
                {
                    cancelToken.ThrowIfCancellationRequested();
                    var channel = GetChannel();

                    using (var sr = new StreamReader(new StreamReadHelper(stream)))
                    {
                        cancelToken.ThrowIfCancellationRequested();
                        EnsureConnected(cancelToken);

                        cancelToken.ThrowIfCancellationRequested();
                        var file = m_telegramClient.UploadFile(remotename, sr, cancelToken).GetAwaiter().GetResult();

                        cancelToken.ThrowIfCancellationRequested();
                        var inputPeerChannel = new TLInputPeerChannel { ChannelId = channel.Id, AccessHash = (long)channel.AccessHash };
                        var fileNameAttribute = new TLDocumentAttributeFilename
                        {
                            FileName = remotename
                        };

                        EnsureConnected(cancelToken);
                        m_telegramClient.SendUploadedDocument(inputPeerChannel, file, remotename, "application/zip", new TLVector<TLAbsDocumentAttribute> { fileNameAttribute }, cancelToken).GetAwaiter().GetResult();
                    }
                },
                nameof(PutAsync));

            return Task.CompletedTask;
        }

        public void Get(string remotename, Stream stream)
        {
            SafeExecute(() =>
                {
                    var fileInfo = ListChannelFileInfos().First(fi => fi.Name == remotename);
                    var fileLocation = fileInfo.ToFileLocation();

                    var limit = MEBIBYTE_IN_BYTES;
                    var currentOffset = 0;


                    while (currentOffset < fileInfo.Size)
                    {
                        try
                        {
                            EnsureConnected();
                            var file = m_telegramClient.GetFile(fileLocation, limit, currentOffset).GetAwaiter().GetResult();
                            stream.Write(file.Bytes, 0, file.Bytes.Length);
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
                },
                $"{nameof(Get)}");
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = File.OpenRead(filename))
            {
                PutAsync(remotename, fs, cancelToken).GetAwaiter().GetResult();
            }

            return Task.CompletedTask;
        }

        public void Get(string remotename, string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                Get(remotename, fs);
            }
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
                        Id = new TLVector<int> { fileInfo.MessageId }
                    };

                    EnsureConnected();
                    m_telegramClient.SendRequestAsync<TLAffectedMessages>(request).GetAwaiter().GetResult();
                },
                $"{nameof(Delete)}({remotename})");
        }

        public List<ChannelFileInfo> ListChannelFileInfos()
        {
            var channel = GetChannel();

            var inputPeerChannel = new TLInputPeerChannel { ChannelId = channel.Id, AccessHash = channel.AccessHash.Value };
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
            new CommandLineArgument(Strings.CHANNEL_NAME, CommandLineArgument.ArgumentType.String, Strings.ChannelName, Strings.ChannelName)
        };

        public string Description { get; } = Strings.Description;

        public string[] DNSName { get; }

        public void Test()
        {
            // No need to use SafeExecute methods
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

            var absChats = GetChats();

            var channel = (TLChannel)absChats.FirstOrDefault(chat => chat is TLChannel tlChannel && tlChannel.Title == m_channelName);
            m_channelCache = channel;
            return channel;
        }

        private IEnumerable<TLAbsChat> GetChats()
        {
            var lastDate = 0;
            while (true)
            {
                var dialogs = m_telegramClient.GetUserDialogsAsync(lastDate).GetAwaiter().GetResult();
                var tlDialogs = dialogs as TLDialogs;
                var tlDialogsSlice = dialogs as TLDialogsSlice;

                foreach (var chat in tlDialogs?.Chats ?? tlDialogsSlice?.Chats)
                {
                    switch (chat)
                    {
                        case TLChannelForbidden _:
                        case TLChatForbidden _:
                            break;
                        case TLChat c:
                            lastDate = c.Date;
                            break;
                        case TLChannel c:
                            lastDate = c.Date;
                            break;
                        default:
                            break;                    }

                    yield return chat;
                }

                if (tlDialogs?.Dialogs?.Count < 100 ||
                    tlDialogsSlice?.Dialogs?.Count < 100)
                {
                    yield break;
                }
            }
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

                    if (string.IsNullOrEmpty(m_authCode))
                    {
                        throw new UserInformationException(Strings.NoAuthCodeError, nameof(Strings.NoAuthCodeError));
                    }
                    else
                    {
                        throw new UserInformationException(Strings.WrongAuthCodeError, nameof(Strings.WrongAuthCodeError));
                    }
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

        private void EnsureConnected(CancellationToken? cancelToken = null)
        {
            var isConnected = false;

            while (isConnected == false)
            {
                cancelToken?.ThrowIfCancellationRequested();

                try
                {
                    isConnected = m_telegramClient.ConnectAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception)
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

        private static void SafeExecute(Action action, string actionName)
        {
            lock (m_lockObj)
            {
                Log.WriteInformationMessage(m_logTag, nameof(Strings.STARTING_EXECUTING), Strings.STARTING_EXECUTING, actionName);
                try
                {
                    action();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UserInformationException uiExc)
                {
                    Log.WriteWarningMessage(m_logTag, nameof(Strings.USER_INFO_EXC), uiExc, Strings.USER_INFO_EXC);
                    throw;
                }
                catch (FloodException floodExc)
                {
                    var randSeconds = new Random().Next(2, 15);
                    Log.WriteInformationMessage(m_logTag, nameof(Strings.TELEGRAM_FLOOD), Strings.TELEGRAM_FLOOD, floodExc.TimeToWait.TotalSeconds + randSeconds);
                    Thread.Sleep(floodExc.TimeToWait + TimeSpan.FromSeconds(randSeconds));
                    SafeExecute(action, actionName);
                }
                catch (Exception e)
                {
                    Log.WriteWarningMessage(m_logTag, nameof(Strings.EXCEPTION_RETRY), e, Strings.EXCEPTION_RETRY);
                    action();
                }
            }

            Log.WriteInformationMessage(m_logTag, nameof(Strings.DONE_EXECUTING), Strings.DONE_EXECUTING, actionName);
        }

        private static T SafeExecute<T>(Func<T> func, string actionName)
        {
            lock (m_lockObj)
            {
                Log.WriteInformationMessage(m_logTag, nameof(Strings.STARTING_EXECUTING), Strings.STARTING_EXECUTING, actionName);
                try
                {
                    var res = func();
                    Log.WriteInformationMessage(m_logTag, nameof(Strings.DONE_EXECUTING), Strings.DONE_EXECUTING, actionName);
                    return res;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UserInformationException uiExc)
                {
                    Log.WriteWarningMessage(m_logTag, nameof(Strings.USER_INFO_EXC), uiExc, Strings.USER_INFO_EXC);
                    throw;
                }
                catch (FloodException floodExc)
                {
                    var randSeconds = new Random().Next(2, 15);
                    Log.WriteInformationMessage(m_logTag, nameof(Strings.TELEGRAM_FLOOD), Strings.TELEGRAM_FLOOD, floodExc.TimeToWait.TotalSeconds + randSeconds);
                    Thread.Sleep(floodExc.TimeToWait);
                    var res = SafeExecute(func, actionName);
                    Log.WriteInformationMessage(m_logTag, nameof(Strings.DONE_EXECUTING), Strings.DONE_EXECUTING, actionName);
                    return res;
                }
                catch (Exception e)
                {
                    Log.WriteErrorMessage(m_logTag, nameof(Strings.EXCEPTION_RETRY), e, Strings.EXCEPTION_RETRY);
                    return func();
                }
            }
        }
    }
}