// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Uri = System.Uri;

namespace Duplicati.Library.Modules.Builtin;

public class SendTelegramMessage : ReportHelper
{
    /// <summary>
    /// The tag used for log messages
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<SendTelegramMessage>();

    /// <summary>
    /// The timeout for the HTTP request
    /// </summary>
    private static readonly TimeSpan REQUEST_TIMEOUT = TimeSpan.FromSeconds(10);

    #region Option names
    /// <summary>
    /// Option used to specify Telegram bot ID
    /// </summary>
    private const string OPTION_BOTID = "send-telegram-bot-id";
    /// <summary>
    /// Option used to specify Telegram bot API key
    /// </summary>
    private const string OPTION_APIKEY = "send-telegram-api-key";
    /// <summary>
    /// Option used to specify channel to send to
    /// </summary>
    private const string OPTION_CHANNEL = "send-telegram-channel-id";
    /// <summary>
    /// Option used to specify the topic ID to be used in telegram groups.
    /// </summary>
    private const string OPTION_TOPICID = "send-telegram-topid-id";
    /// <summary>
    /// Option used to specify report body
    /// </summary>
    private const string OPTION_MESSAGE = "send-telegram-message";
    /// <summary>
    /// Option used to specify report level
    /// </summary>
    private const string OPTION_SENDLEVEL = "send-telegram-level";
    /// <summary>
    /// Option used to specify if reports are sent for other operations than backups
    /// </summary>
    private const string OPTION_SENDALL = "send-telegram-any-operation";
    /// <summary>
    /// Option used to specify what format the result is sent in.
    /// </summary>
    private const string OPTION_RESULT_FORMAT = "send-telegram-result-output-format";

    /// <summary>
    /// Option used to set the log level
    /// </summary>
    private const string OPTION_LOG_LEVEL = "send-telegram-log-level";
    /// <summary>
    /// Option used to set the log level
    /// </summary>
    private const string OPTION_LOG_FILTER = "send-telegram-log-filter";
    /// <summary>
    /// Option used to set the maximum number of log lines
    /// </summary>
    private const string OPTION_MAX_LOG_LINES = "send-telegram-max-log-lines";
    
    /// <summary>
    /// Telegram message maximum length.
    /// </summary>
    private const int TELEGRAM_MAX_LENGTH = 4096;

    #endregion

    #region Option defaults
    /// <summary>
    /// The default message body
    /// </summary>
    protected override string DEFAULT_BODY => string.Format("Duplicati %OPERATIONNAME% report for %backup-name%{0}{0} %RESULT%", Environment.NewLine);
    /// <summary>
    /// Don't use the subject for telegram
    /// </summary>
    protected override string DEFAULT_SUBJECT => string.Empty;
    #endregion


    #region Implementation of IGenericModule

    /// <summary>
    /// The module key, used to activate or deactivate the module on the commandline
    /// </summary>
    public override string Key => "sendtelegram";

    /// <summary>
    /// A localized string describing the module with a friendly name
    /// </summary>
    public override string DisplayName => Strings.SendTelegramMessage.DisplayName;

    /// <summary>
    /// A localized description of the module
    /// </summary>
    public override string Description => Strings.SendTelegramMessage.Description;

    /// <summary>
    /// A boolean value that indicates if the module should always be loaded.
    /// If true, the  user can choose to not load the module by entering the appropriate commandline option.
    /// If false, the user can choose to load the module by entering the appropriate commandline option.
    /// </summary>
    public override bool LoadAsDefault => true;

    /// <summary>
    /// Gets a list of supported commandline arguments
    /// </summary>
    public override IList<ICommandLineArgument> SupportedCommands
        => [
            new CommandLineArgument(OPTION_CHANNEL, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendTelegramChannelShort, Strings.SendTelegramMessage.SendTelegramChannelLong),
            new CommandLineArgument(OPTION_MESSAGE, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendTelegramMessageShort, Strings.SendTelegramMessage.SendTelegramMessageLong, DEFAULT_BODY),
            new CommandLineArgument(OPTION_BOTID, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendTelegramBotIdShort, Strings.SendTelegramMessage.SendTelegramBotIdLong),
            new CommandLineArgument(OPTION_APIKEY, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendTelegramApiKeyShort, Strings.SendTelegramMessage.SendTelegramApiKeyLong),
            new CommandLineArgument(OPTION_SENDLEVEL, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendTelegramLevelShort, Strings.SendTelegramMessage.SendTelegramlevelLong(ParsedResultType.Success.ToString(), ParsedResultType.Warning.ToString(), ParsedResultType.Error.ToString(), ParsedResultType.Fatal.ToString(), "All"), DEFAULT_LEVEL, null, Enum.GetNames(typeof(ParsedResultType)).Union(new string[] { "All" } ).ToArray()),
            new CommandLineArgument(OPTION_SENDALL, CommandLineArgument.ArgumentType.Boolean, Strings.SendTelegramMessage.SendTelegramManyOperationShort, Strings.SendTelegramMessage.SendTelegramManyOperationLong),
            new CommandLineArgument(OPTION_LOG_LEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.OptionLoglevelShort, Strings.ReportHelper.OptionLoglevelLong, DEFAULT_LOG_LEVEL.ToString(), null, Enum.GetNames(typeof(Logging.LogMessageType))),
            new CommandLineArgument(OPTION_LOG_FILTER, CommandLineArgument.ArgumentType.String, Strings.ReportHelper.OptionLogfilterShort, Strings.ReportHelper.OptionLogfilterLong),
            new CommandLineArgument(OPTION_MAX_LOG_LINES, CommandLineArgument.ArgumentType.Integer, Strings.ReportHelper.OptionmaxloglinesShort, Strings.ReportHelper.OptionmaxloglinesLong, DEFAULT_LOGLINES.ToString()),
            new CommandLineArgument(OPTION_RESULT_FORMAT, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.ResultFormatShort, Strings.ReportHelper.ResultFormatLong(Enum.GetNames(typeof(ResultExportFormat))), DEFAULT_EXPORT_FORMAT.ToString(), null, Enum.GetNames(typeof(ResultExportFormat))),
            new CommandLineArgument(OPTION_TOPICID, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendTelegramTopicShort, Strings.SendTelegramMessage.SendTelegramTopicLong),
        ];
    
    protected override string SubjectOptionName => OPTION_MESSAGE;
    protected override string BodyOptionName => OPTION_MESSAGE;
    protected override string ActionLevelOptionName => OPTION_SENDLEVEL;
    protected override string ActionOnAnyOperationOptionName => OPTION_SENDALL;
    protected override string LogLevelOptionName => OPTION_LOG_LEVEL;
    protected override string LogFilterOptionName => OPTION_LOG_FILTER;
    protected override string LogLinesOptionName => OPTION_MAX_LOG_LINES;
    protected override string ResultFormatOptionName => OPTION_RESULT_FORMAT;

    /// <summary>
    /// The server username
    /// </summary>
    private string m_botid;
    /// <summary>
    /// The server password
    /// </summary>
    private string m_apikey;
    /// <summary>
    /// The Telegram ChannelID
    /// </summary>
    private string m_channelId;
    /// <summary>
    /// The telegram TopicID
    /// </summary>
    private string m_topicId;

    /// <summary>
    /// This method is the interception where the module can interact with the execution environment and modify the settings.
    /// </summary>
    /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
    protected override bool ConfigureModule(IDictionary<string, string> commandlineOptions)
    {
        //We need at least a recipient
        commandlineOptions.TryGetValue(OPTION_CHANNEL, out m_channelId);
        if (string.IsNullOrEmpty(m_channelId))
            return false;

        commandlineOptions.TryGetValue(OPTION_BOTID, out m_botid);
        commandlineOptions.TryGetValue(OPTION_APIKEY, out m_apikey);
        commandlineOptions.TryGetValue(OPTION_TOPICID, out m_topicId);

        return true;
    }

    #endregion
    
    /// <inheritdoc />
    protected override string ReplaceTemplate(string input, object result, Exception exception, bool subjectline)
    {
        // No need to do the expansion as we throw away the result
        return subjectline ? string.Empty : base.ReplaceTemplate(input, result, exception, subjectline);
    }
    
    /// <summary>
    /// Sends message to telegram
    /// </summary>
    /// <param name="subject">Header to add to message</param>
    /// <param name="body">Body of message</param>
    protected override async void SendMessage(string subject, string body)
    {
        try
        {
            if (string.IsNullOrEmpty(body))
                return; 
            
            // Combine subject and body
            body = string.Join(Environment.NewLine, subject, body);

            // Split message into chunks if needed
            var messages = body.Length <= TELEGRAM_MAX_LENGTH 
                ? [body]
                : Enumerable.Range(0, (body.Length + TELEGRAM_MAX_LENGTH - 1) / TELEGRAM_MAX_LENGTH)
                    .Select(i => body.Substring(
                        i * TELEGRAM_MAX_LENGTH,
                        Math.Min(TELEGRAM_MAX_LENGTH, body.Length - i * TELEGRAM_MAX_LENGTH)))
                    .ToArray();

            // Send all chunks sequentially
            for (var i = 0; i < messages.Length; i++)
                await SendMessageChunk(messages[i], i + 1, messages.Length).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "telegramSendError", e, 
                "Failed to process message for telegram: {0}", e.Message);
        }
    }
    
    /// <summary>
    /// Sends the message/partial message to telegram
    /// </summary>
    /// <param name="message">body or partial body to send</param>
    /// <param name="partNumber">Part number</param>
    /// <param name="totalParts">Total parts in the original message</param>
    private async Task SendMessageChunk(string message, int partNumber, int totalParts)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(m_botid))
                throw new Exception("Telegram Bot ID is required and not set");
            if (string.IsNullOrWhiteSpace(m_apikey))
                throw new Exception("Telegram API Key is required and not set");
            
            var p = new
            {
                chat_id = Uri.EscapeDataString(m_channelId),
                message_thread_id = string.IsNullOrWhiteSpace(m_topicId) ? null : Uri.EscapeDataString(m_topicId),
                parse_mode = "Markdown",
                text = Uri.EscapeDataString(totalParts > 1 
                    ? $"Part {partNumber}/{totalParts}:\n{message}"
                    : message),
                botId = Uri.EscapeDataString(m_botid),
                apiKey = Uri.EscapeDataString(m_apikey)
            };

            var baseUrl = $"https://api.telegram.org/bot{p.botId}:{p.apiKey}/sendMessage";
            var url = $"{baseUrl}?chat_id={p.chat_id}&parse_mode={p.parse_mode}&text={p.text}" +
                      $"{(!string.IsNullOrWhiteSpace(m_topicId) ? $"&message_thread_id={m_topicId}" : string.Empty)}";
            
            using var timeoutToken = new CancellationTokenSource();
            timeoutToken.CancelAfter(REQUEST_TIMEOUT);

            using var client = HttpClientHelper.CreateClient();
            var response = await client.GetAsync(url, timeoutToken.Token).ConfigureAwait(false);
            var responseContent = await response.Content.ReadAsStringAsync(timeoutToken.Token).ConfigureAwait(false);

            if (!responseContent.Contains("\"ok\":true"))
                Logging.Log.WriteWarningMessage(LOGTAG, "telegramSendError", null, 
                    "Failed to send to telegram messages part {0}/{1}: {2}", 
                    partNumber, totalParts, responseContent);
        }
        catch (Exception e)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "telegramSendError", e, 
                "Failed to send to telegram messages part {0}/{1}: {2}", 
                partNumber, totalParts, e.Message);
        }
    }
}
