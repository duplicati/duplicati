using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Duplicati.Library.Interface;

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
            new CommandLineArgument(OPTION_CHANNEL, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendtelegramchannelShort, Strings.SendTelegramMessage.SendtelegramchannelLong),
            new CommandLineArgument(OPTION_MESSAGE, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendtelegrammessageShort, Strings.SendTelegramMessage.SendtelegrammessageLong, DEFAULT_BODY),
            new CommandLineArgument(OPTION_BOTID, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendtelegrambotidShort, Strings.SendTelegramMessage.SendtelegrambotidLong),
            new CommandLineArgument(OPTION_APIKEY, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendtelegramapikeyShort, Strings.SendTelegramMessage.SendtelegramapikeyLong),
            new CommandLineArgument(OPTION_SENDLEVEL, CommandLineArgument.ArgumentType.String, Strings.SendTelegramMessage.SendtelegramlevelShort, Strings.SendTelegramMessage.SendtelegramlevelLong(ParsedResultType.Success.ToString(), ParsedResultType.Warning.ToString(), ParsedResultType.Error.ToString(), ParsedResultType.Fatal.ToString(), "All"), DEFAULT_LEVEL, null, Enum.GetNames(typeof(ParsedResultType)).Union(new string[] { "All" } ).ToArray()),
            new CommandLineArgument(OPTION_SENDALL, CommandLineArgument.ArgumentType.Boolean, Strings.SendTelegramMessage.SendtelegramanyoperationShort, Strings.SendTelegramMessage.SendtelegramanyoperationLong),

            new CommandLineArgument(OPTION_LOG_LEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.OptionLoglevelShort, Strings.ReportHelper.OptionLoglevelLong, DEFAULT_LOG_LEVEL.ToString(), null, Enum.GetNames(typeof(Logging.LogMessageType))),
            new CommandLineArgument(OPTION_LOG_FILTER, CommandLineArgument.ArgumentType.String, Strings.ReportHelper.OptionLogfilterShort, Strings.ReportHelper.OptionLogfilterLong),
            new CommandLineArgument(OPTION_MAX_LOG_LINES, CommandLineArgument.ArgumentType.Integer, Strings.ReportHelper.OptionmaxloglinesShort, Strings.ReportHelper.OptionmaxloglinesLong, DEFAULT_LOGLINES.ToString()),

            new CommandLineArgument(OPTION_RESULT_FORMAT, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.ResultFormatShort, Strings.ReportHelper.ResultFormatLong(Enum.GetNames(typeof(ResultExportFormat))), DEFAULT_EXPORT_FORMAT.ToString(), null, Enum.GetNames(typeof(ResultExportFormat))),
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

        return true;
    }

    #endregion

    protected override string ReplaceTemplate(string input, object result, Exception exception, bool subjectline)
    {
        // No need to do the expansion as we throw away the result
        if (subjectline)
            return string.Empty;
        return base.ReplaceTemplate(input, result, exception, subjectline);
    }

    protected override async void SendMessage(string subject, string body)
    {
        try
        {

            var p = new
            {
                chat_id = Uri.EscapeDataString(m_channelId),
                parse_mode = "Text",
                text = Uri.EscapeDataString(body),
                botId = Uri.EscapeDataString(m_botid),
                apiKey = Uri.EscapeDataString(m_apikey)
            };
            var url = $"https://api.telegram.org/bot{p.botId}:{p.apiKey}/sendMessage?chat_id={p.chat_id}&parse_mode={p.parse_mode}&text={p.text}";

            using var client = new HttpClient { Timeout = REQUEST_TIMEOUT };
            var response = await client.GetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (responseContent.Contains("\"ok\":true"))
                return;

            Logging.Log.WriteWarningMessage(LOGTAG, "telegramSendError", null, "Failed to send to telegram messages: {0}", responseContent);
        }
        catch (Exception e)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "telegramSendError", e, "Failed to send to telegram messages: {0}", e.Message);
        }
    }

}
