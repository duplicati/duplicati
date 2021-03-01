using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;
using Matrix;
using Matrix.Xmpp.Client;
using Enum = System.Enum;

namespace Duplicati.Library.Modules.Builtin
{
    public class SendJabberMessage : ReportHelper
    {
        /// <summary>
        /// The tag used for log messages
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<SendJabberMessage>();

        #region Option names
        /// <summary>
        /// Option used to specify server username
        /// </summary>
        private const string OPTION_USERNAME = "send-xmpp-username";
        /// <summary>
        /// Option used to specify server password
        /// </summary>
        private const string OPTION_PASSWORD = "send-xmpp-password";
        /// <summary>
        /// Option used to specify recipient(s)
        /// </summary>
        private const string OPTION_RECIPIENT = "send-xmpp-to";
        /// <summary>
        /// Option used to specify report body
        /// </summary>
        private const string OPTION_MESSAGE = "send-xmpp-message";
        /// <summary>
        /// Option used to specify report level
        /// </summary>
        private const string OPTION_SENDLEVEL = "send-xmpp-level";
        /// <summary>
        /// Option used to specify if reports are sent for other operations than backups
        /// </summary>
        private const string OPTION_SENDALL = "send-xmpp-any-operation";
        /// <summary>
        /// Option used to specify what format the result is sent in.
        /// </summary>
        private const string OPTION_RESULT_FORMAT = "send-xmpp-result-output-format";

        /// <summary>
        /// Option used to set the log level
        /// </summary>
        private const string OPTION_LOG_LEVEL = "send-xmpp-log-level";
        /// <summary>
        /// Option used to set the log level
        /// </summary>
        private const string OPTION_LOG_FILTER = "send-xmpp-log-filter";
        /// <summary>
        /// Option used to set the maximum number of log lines
        /// </summary>
        private const string OPTION_MAX_LOG_LINES = "send-xmpp-max-log-lines";

        #endregion

        #region Option defaults
        /// <summary>
        /// The default message body
        /// </summary>
        protected override string DEFAULT_BODY => string.Format("Duplicati %OPERATIONNAME% report for %backup-name%{0}{0}%RESULT%", Environment.NewLine);
        /// <summary>
        /// Don't use the subject for XMPP
        /// </summary>
        protected override string DEFAULT_SUBJECT => string.Empty;
        #endregion

        #region Private variables
        /// <summary>
        /// The server username
        /// </summary>
        private string m_username;
        /// <summary>
        /// The server password
        /// </summary>
        private string m_password;
        /// <summary>
        /// The XMPP recipient
        /// </summary>
        private string m_to;

        #endregion


        #region Implementation of IGenericModule

        /// <summary>
        /// The module key, used to activate or deactivate the module on the commandline
        /// </summary>
        public override string Key { get { return "sendxmpp"; } }

        /// <summary>
        /// A localized string describing the module with a friendly name
        /// </summary>
        public override string DisplayName { get { return Strings.SendJabberMessage.DisplayName;} }

        /// <summary>
        /// A localized description of the module
        /// </summary>
        public override string Description { get { return Strings.SendJabberMessage.Description; } }

        /// <summary>
        /// A boolean value that indicates if the module should always be loaded.
        /// If true, the  user can choose to not load the module by entering the appropriate commandline option.
        /// If false, the user can choose to load the module by entering the appropriate commandline option.
        /// </summary>
        public override bool LoadAsDefault { get { return true; } }

        /// <summary>
        /// Gets a list of supported commandline arguments
        /// </summary>
        public override IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(OPTION_RECIPIENT, CommandLineArgument.ArgumentType.String, Strings.SendJabberMessage.SendxmpptoShort, Strings.SendJabberMessage.SendxmpptoLong),
                    new CommandLineArgument(OPTION_MESSAGE, CommandLineArgument.ArgumentType.String, Strings.SendJabberMessage.SendxmppmessageShort, Strings.SendJabberMessage.SendxmppmessageLong, DEFAULT_BODY),
                    new CommandLineArgument(OPTION_USERNAME, CommandLineArgument.ArgumentType.String, Strings.SendJabberMessage.SendxmppusernameShort, Strings.SendJabberMessage.SendxmppusernameLong),
                    new CommandLineArgument(OPTION_PASSWORD, CommandLineArgument.ArgumentType.String, Strings.SendJabberMessage.SendxmpppasswordShort, Strings.SendJabberMessage.SendxmpppasswordLong),
                    new CommandLineArgument(OPTION_SENDLEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.SendJabberMessage.SendxmpplevelShort, Strings.SendJabberMessage.SendxmpplevelLong(ParsedResultType.Success.ToString(), ParsedResultType.Warning.ToString(), ParsedResultType.Error.ToString(), ParsedResultType.Fatal.ToString(), "All"), DEFAULT_LEVEL, null, Enum.GetNames(typeof(ParsedResultType)).Union(new string[] { "All" } ).ToArray()),
                    new CommandLineArgument(OPTION_SENDALL, CommandLineArgument.ArgumentType.Boolean, Strings.SendJabberMessage.SendxmppanyoperationShort, Strings.SendJabberMessage.SendxmppanyoperationLong),

                    new CommandLineArgument(OPTION_LOG_LEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.OptionLoglevellShort, Strings.ReportHelper.OptionLoglevelLong, DEFAULT_LOG_LEVEL.ToString(), null, Enum.GetNames(typeof(Logging.LogMessageType))),
                    new CommandLineArgument(OPTION_LOG_FILTER, CommandLineArgument.ArgumentType.String, Strings.ReportHelper.OptionLogfilterShort, Strings.ReportHelper.OptionLogfilterLong),
                    new CommandLineArgument(OPTION_MAX_LOG_LINES, CommandLineArgument.ArgumentType.Integer, Strings.ReportHelper.OptionmaxloglinesShort, Strings.ReportHelper.OptionmaxloglinesLong, DEFAULT_LOGLINES.ToString()),

                    new CommandLineArgument(OPTION_RESULT_FORMAT, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.ResultFormatShort, Strings.ReportHelper.ResultFormatLong(Enum.GetNames(typeof(ResultExportFormat))), DEFAULT_EXPORT_FORMAT.ToString(), null, Enum.GetNames(typeof(ResultExportFormat))),
                });
            }
        }

        protected override string SubjectOptionName => OPTION_MESSAGE;
        protected override string BodyOptionName => OPTION_MESSAGE;
        protected override string ActionLevelOptionName => OPTION_SENDLEVEL;
        protected override string ActionOnAnyOperationOptionName => OPTION_SENDALL;
        protected override string LogLevelOptionName => OPTION_LOG_LEVEL;
        protected override string LogFilterOptionName => OPTION_LOG_FILTER;
        protected override string LogLinesOptionName => OPTION_MAX_LOG_LINES;
        protected override string ResultFormatOptionName => OPTION_RESULT_FORMAT;

        /// <summary>
        /// This method is the interception where the module can interact with the execution environment and modify the settings.
        /// </summary>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
        protected override bool ConfigureModule(IDictionary<string, string> commandlineOptions)
        {
            //We need at least a recipient
            commandlineOptions.TryGetValue(OPTION_RECIPIENT, out m_to);
            if (string.IsNullOrEmpty(m_to))
                return false;

            commandlineOptions.TryGetValue(OPTION_USERNAME, out m_username);
            commandlineOptions.TryGetValue(OPTION_PASSWORD, out m_password);

            return true;
        }

        #endregion

        protected override string ReplaceTemplate(string input, object result, bool subjectline)
        {
            // No need to do the expansion as we throw away the result
            if (subjectline)
                return string.Empty;
            return base.ReplaceTemplate(input, result, subjectline);
        }

        protected override async void SendMessage(string subject, string body)
        {
            Exception ex = null;
            var waitEvent = new System.Threading.ManualResetEvent(false);

            var uri = new Library.Utility.Uri(m_username.Contains("://") ? m_username : "http://" + m_username);

            var resource = uri.Path ?? "";
            if (resource.StartsWith("/", StringComparison.Ordinal))
                resource = resource.Substring(1);
            
            var xmppClient = new XmppClient
            {
                Username = uri.Username,
                Password = string.IsNullOrWhiteSpace(m_password) ? uri.Password : m_password,
                XmppDomain = uri.Host,
                Port = uri.Port == -1 ? (uri.Scheme == "https" ? 5223 : 5222) : uri.Port,
                Tls = uri.Scheme == "https",
                Resource = string.IsNullOrWhiteSpace(resource) ? "Duplicati" : resource,
                HostnameResolver = new Matrix.Network.Resolver.NameResolver()
            };

            // connect so the server
            await xmppClient.ConnectAsync();

            try
            {
                foreach (var recipient in m_to.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries))
                    await xmppClient.SendAsync(new Message(recipient, body, subject));
            }
            catch (Exception e)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "XMPPSendError", e, "Failed to send to XMPP messages: {0}", e.Message);
                ex = e;
            }
            finally
            {
                waitEvent.Set();
            }

            var timeout = !waitEvent.WaitOne(TimeSpan.FromSeconds(30), true);

            try
            {
                // Close connection again
                await xmppClient.DisconnectAsync();
            }
            catch (Exception lex)
            {
                Logging.Log.WriteExplicitMessage(LOGTAG, "CloseConnectionError", lex, "Failed to close XMPP connection: {0}", lex.Message);
            }

            if (ex != null)
                throw ex;
            if (timeout)
                throw new TimeoutException(Strings.SendJabberMessage.LoginTimeoutError);
        }
    }
}
