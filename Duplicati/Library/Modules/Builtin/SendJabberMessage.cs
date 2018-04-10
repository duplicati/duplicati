using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Modules.Builtin
{
    public class SendJabberMessage : ReportHelper
    {
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

        protected override void SendMessage(string subject, string message)
        {
            Exception ex = null;
            var waitEvent = new System.Threading.ManualResetEvent(false);

            var uri = new Library.Utility.Uri(m_username.Contains("://") ? m_username : "http://" + m_username);
            var con = new agsXMPP.XmppClientConnection(uri.Host, uri.Port == -1 ? (uri.Scheme == "https" ? 5223 :5222) : uri.Port);
            if (uri.Scheme == "https")
                con.UseSSL = true;

            var resource = uri.Path ?? "";
            if (resource.StartsWith("/", StringComparison.Ordinal))
                resource = resource.Substring(1);

            if (string.IsNullOrWhiteSpace(resource))
                resource = "Duplicati";

            agsXMPP.ObjectHandler loginDelegate = (sender) =>
            {
                try
                {
                    foreach(var recipient in m_to.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries))
                        con.Send(new agsXMPP.protocol.client.Message(recipient, agsXMPP.protocol.client.MessageType.chat, message));
                }
                catch (Exception e)
                {
                    ex = e;
                }
                finally
                {
                    waitEvent.Set();
                }
            };

            agsXMPP.ErrorHandler errorHandler = (sender, e) => {
                ex = e;
                waitEvent.Set();
            };

            agsXMPP.XmppElementHandler loginErroHandler = (sender, e) => {
                ex = new Exception(string.Format("Failed to log in: {0}", e));
                waitEvent.Set();
            };
    
            con.OnLogin += loginDelegate;
            con.OnError += errorHandler;
            con.OnAuthError += loginErroHandler;
            //con.OnBinded += (sender) => {Console.WriteLine("Binded: {0}", sender);};
            //con.OnIq += (sender, iq) => {Console.WriteLine("Iq: {0}", iq);};
            //con.OnReadXml += (sender, xml) => {Console.WriteLine("ReadXml: {0}", xml);};
            //con.OnWriteXml += (sender, xml) => {Console.WriteLine("writeXml: {0}", xml);};;
            con.Open(uri.Username, string.IsNullOrWhiteSpace(m_password) ? uri.Password : m_password, resource);

            var timeout = !waitEvent.WaitOne(TimeSpan.FromSeconds(30), true);

            con.OnLogin -= loginDelegate;
            con.OnError -= errorHandler;
            con.OnAuthError -= loginErroHandler;

            try
            {
                con.Close();
            }
            catch
            {
            }

            if (ex != null)
                throw ex;
            if (timeout)
                throw new TimeoutException(Strings.SendJabberMessage.LoginTimeoutError);
        }

        /// <summary>
        /// Helper method to perform template expansion
        /// </summary>
        /// <returns>The expanded template.</returns>
        /// <param name="input">The input template.</param>
        /// <param name="result">The result object.</param>
        /// <param name="subjectline">If set to <c>true</c>, the result is intended for a subject or title line.</param>
        protected override string ReplaceTemplate(string input, object result, bool subjectline)
        {
            if (subjectline)
                return string.Empty;
            else
                return base.ReplaceTemplate(input, result, subjectline);                
        }
    }
}
