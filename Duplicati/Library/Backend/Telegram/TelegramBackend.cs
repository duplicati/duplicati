using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Extensions;
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
    public class Telegram : IStreamingBackend
    {
        private readonly TelegramClient m_telegramClient;
        private readonly EncryptedFileSessionStore m_encSessionStore;

        private readonly object m_lockObj = new object();

        private readonly int m_apiId;
        private readonly string m_apiHash;
        private readonly string m_authCode;
        private readonly string m_password;
        private readonly string m_channelName;
        private readonly string m_phoneNumber;
        private TLChannel m_channelCache;

        private static string m_phoneCodeHash;
        private static readonly string m_logTag = Log.LogTagFromType(typeof(Telegram));
        private const int BYTES_IN_MEBIBYTE = 1048576;

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

            m_encSessionStore = new EncryptedFileSessionStore($"{m_apiHash}_{m_apiId}");
            m_telegramClient = new TelegramClient(m_apiId, m_apiHash, m_encSessionStore, m_phoneNumber);
        }

        public void Dispose()
        {
            m_telegramClient?.Dispose();
        }

        public string DisplayName { get; } = Strings.DisplayName;
        public string ProtocolKey { get; } = "telegram";

        public IEnumerable<IFileEntry> List()
        {
            return StartActionAsync<IEnumerable<IFileEntry>>(async () =>
                {
                    await AuthenticateAsync();
                    await EnsureChannelCreatedAsync();
                    var fileInfos = await ListChannelFileInfosAsync();
                    var result = fileInfos.Select(fi => fi.ToFileEntry());
                    return result;
                }, nameof(List)).Result;
        }

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            await StartActionAsync(async () =>
                {
                    cancelToken.ThrowIfCancellationRequested();

                    await AuthenticateAsync();

                    cancelToken.ThrowIfCancellationRequested();

                    var channel = GetChannel();

                    cancelToken.ThrowIfCancellationRequested();

                    using (var sr = new StreamReader(new StreamReadHelper(stream)))
                    {
                        cancelToken.ThrowIfCancellationRequested();
                        await EnsureConnectedAsync(cancelToken);

                        cancelToken.ThrowIfCancellationRequested();
                        var file = await m_telegramClient.UploadFile(remotename, sr, cancelToken);

                        cancelToken.ThrowIfCancellationRequested();
                        var inputPeerChannel = new TLInputPeerChannel { ChannelId = channel.Id, AccessHash = (long)channel.AccessHash };
                        var fileNameAttribute = new TLDocumentAttributeFilename { FileName = remotename };

                        await EnsureConnectedAsync(cancelToken);

                        lock (m_lockObj)
                        {
                            m_telegramClient.SendUploadedDocument(inputPeerChannel, file, remotename, "application/zip", new TLVector<TLAbsDocumentAttribute> { fileNameAttribute }, cancelToken).GetAwaiter().GetResult();
                        }
                    }
                }, nameof(PutAsync));
        }

        public void Get(string remotename, Stream stream)
        {
            StartActionAsync(async () =>
                {
                    var fileInfo = ListChannelFileInfosAsync().Result.First(fi => fi.Name == remotename);
                    var fileLocation = fileInfo.ToFileLocation();

                    var limit = BYTES_IN_MEBIBYTE;
                    var currentOffset = 0;

                    while (currentOffset < fileInfo.Size)
                    {
                        try
                        {
                            await EnsureConnectedAsync();
                            var file = await m_telegramClient.GetFile(fileLocation, limit, currentOffset);
                            await stream.WriteAsync(file.Bytes, 0, file.Bytes.Length);
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
                }, $"{nameof(Get)}").Wait();
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (var fs = File.OpenRead(filename))
            {
                await PutAsync(remotename, fs, cancelToken);
            }
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
            StartActionAsync(async () =>
                {
                    var channel = GetChannel();
                    var fileInfo = ListChannelFileInfosAsync().Result.FirstOrDefault(fi => fi.Name == remotename);
                    if (fileInfo == null)
                    {
                        return;
                    }

                    var request = new TLRequestDeleteMessages { Channel = new TLInputChannel { ChannelId = channel.Id, AccessHash = channel.AccessHash.Value }, Id = new TLVector<int> { fileInfo.MessageId } };

                    await EnsureConnectedAsync();
                    await m_telegramClient.SendRequestAsync<TLAffectedMessages>(request);
                }, $"{nameof(Delete)}({remotename})").Wait();
        }

        public async Task<List<ChannelFileInfo>> ListChannelFileInfosAsync()
        {
            var channel = GetChannel();

            var inputPeerChannel = new TLInputPeerChannel { ChannelId = channel.Id, AccessHash = channel.AccessHash.Value };
            var result = new List<ChannelFileInfo>();
            var oldMinDate = 0L;
            var newMinDate = (long?)null;

            while (oldMinDate != newMinDate)
            {
                oldMinDate = newMinDate ?? 0L;
                await RetrieveMessagesAsync(inputPeerChannel, result, oldMinDate);
                if (result.Any() == false)
                {
                    break;
                }

                newMinDate = result.Min(cfi => Utility.Utility.NormalizeDateTimeToEpochSeconds(cfi.Date));
            }

            result = result.Distinct().ToList();
            return result;
        }

        private async Task RetrieveMessagesAsync(TLInputPeerChannel inputPeerChannel, List<ChannelFileInfo> result, long offsetDate)
        {
            await EnsureConnectedAsync();
            var absHistory = m_telegramClient.GetHistoryAsync(inputPeerChannel, offsetDate: (int)offsetDate).GetAwaiter().GetResult();
            var history = ((TLChannelMessages)absHistory).Messages.OfType<TLMessage>();

            foreach (var msg in history)
            {
                if (msg.Media is TLMessageMediaDocument media &&
                    media.Document is TLDocument mediaDoc)
                {
                    var fileInfo = new ChannelFileInfo(
                        msg.Id,
                        mediaDoc.AccessHash,
                        mediaDoc.Id,
                        mediaDoc.Version,
                        mediaDoc.Size,
                        media.Caption,
                        DateTimeOffset.FromUnixTimeSeconds(msg.Date).UtcDateTime);

                    result.Add(fileInfo);
                }
            }
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
            StartActionAsync(AuthenticateAsync, nameof(AuthenticateAsync)).Wait();
        }

        public void CreateFolder()
        {
            StartActionAsync(async () =>
                {
                    await AuthenticateAsync();
                    await EnsureChannelCreatedAsync();
                }, nameof(CreateFolder)).Wait();
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
                EnsureConnectedAsync().Wait();
                var dialogsTask = m_telegramClient.GetUserDialogsAsync(lastDate);
                var waitTask = Task.WhenAny(dialogsTask, Task.Delay(15000)).Result;
                if (waitTask != dialogsTask as Task)
                {
                    throw new TimeoutException("Couldn't get user dialogs");
                }

                var dialogs = dialogsTask.Result;
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
                            throw new NotSupportedException($"Unsupported chat type {chat.GetType()}");
                    }

                    yield return chat;
                }

                if (tlDialogs?.Dialogs?.Count < 100 || tlDialogsSlice?.Dialogs?.Count < 100)
                {
                    yield break;
                }
            }
        }

        private async Task EnsureChannelCreatedAsync()
        {
            var channel = GetChannel();
            if (channel == null)
            {
                var newGroup = new TLRequestCreateChannel { Broadcast = false, Megagroup = false, Title = m_channelName, About = string.Empty };

                await EnsureConnectedAsync();
                await m_telegramClient.SendRequestAsync<object>(newGroup);
            }
        }

        private async Task AuthenticateAsync()
        {
            await EnsureConnectedAsync();

            if (m_telegramClient.IsUserAuthorized())
            {
                return;
            }

            try
            {
                if (m_phoneCodeHash == null)
                {
                    await EnsureConnectedAsync();
                    var phoneCodeHash = await m_telegramClient.SendCodeRequestAsync(m_phoneNumber);
                    SetPhoneCodeHash(phoneCodeHash);
                    m_telegramClient.Session.Save();

                    if (string.IsNullOrEmpty(m_authCode))
                    {
                        throw new UserInformationException(Strings.NoAuthCodeError, nameof(Strings.NoAuthCodeError));
                    }

                    throw new UserInformationException(Strings.WrongAuthCodeError, nameof(Strings.WrongAuthCodeError));
                }

                await m_telegramClient.MakeAuthAsync(m_phoneNumber, m_phoneCodeHash, m_authCode);
            }
            catch (CloudPasswordNeededException)
            {
                if (string.IsNullOrEmpty(m_password))
                {
                    m_telegramClient.Session.Save();
                    throw new UserInformationException(Strings.NoPasswordError, nameof(Strings.NoPasswordError));
                }

                await EnsureConnectedAsync();
                var passwordSetting = await m_telegramClient.GetPasswordSetting();
                await m_telegramClient.MakeAuthWithPasswordAsync(passwordSetting, m_password);
            }

            m_telegramClient.Session.Save();
        }

        private async Task EnsureConnectedAsync(CancellationToken cancelToken = default(CancellationToken))
        {
            if (m_telegramClient.IsReallyConnected())
            {
                return;
            }

            cancelToken.ThrowIfCancellationRequested();

            await m_telegramClient.WrapperConnectAsync(cancelToken);

            cancelToken.ThrowIfCancellationRequested();

            if (m_telegramClient.IsReallyConnected() == false)
            {
                throw new WebException("Unable to connect to telegram");
            }
        }

        private static void SetPhoneCodeHash(string phoneCodeHash)
        {
            m_phoneCodeHash = phoneCodeHash;
        }

        private async Task StartActionAsync(Func<Task> func, string actionName)
        {
            Log.WriteInformationMessage(m_logTag, nameof(Strings.STARTING_EXECUTING), Strings.STARTING_EXECUTING, actionName);
            try
            {
                await func();
            }
            catch (Exception e)
            {
                HandleOrThrowException(e);
                await StartActionAsync(func, actionName);
            }

            Log.WriteInformationMessage(m_logTag, nameof(Strings.DONE_EXECUTING), Strings.DONE_EXECUTING, actionName);
        }

        private async Task<T> StartActionAsync<T>(Func<Task<T>> func, string actionName)
        {
            Log.WriteInformationMessage(m_logTag, nameof(Strings.STARTING_EXECUTING), Strings.STARTING_EXECUTING, actionName);
            try
            {
                var res = await func();
                Log.WriteInformationMessage(m_logTag, nameof(Strings.DONE_EXECUTING), Strings.DONE_EXECUTING, actionName);
                return res;
            }
            catch (Exception e)
            {
                HandleOrThrowException(e);
                return await StartActionAsync(func, actionName);
            }
        }

        private void HandleOrThrowException(Exception e)
        {
            switch (e)
            {
                case UserInformationException uiExc:
                    Log.WriteWarningMessage(m_logTag, nameof(Strings.USER_INFO_EXC), uiExc, Strings.USER_INFO_EXC);
                    throw e;

                case FloodException floodExc:
                    var randSeconds = new Random().Next(2, 15);
                    Log.WriteInformationMessage(m_logTag, nameof(Strings.TELEGRAM_FLOOD), Strings.TELEGRAM_FLOOD, floodExc.TimeToWait.TotalSeconds + randSeconds);
                    Thread.Sleep(floodExc.TimeToWait + TimeSpan.FromSeconds(randSeconds));
                    break;

                case AggregateException aggrExc:
                    HandleOrThrowException(aggrExc.InnerException);
                    break;
            }

            throw e;
        }
    }
}