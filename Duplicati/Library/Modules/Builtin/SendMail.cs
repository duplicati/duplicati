using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MailKit.Net.Smtp;
using MimeKit;
using Duplicati.Library.Modules.Builtin.ResultSerialization;
using DnsClient;

namespace Duplicati.Library.Modules.Builtin
{
    public class SendMail : ReportHelper
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<SendMail>();

        #region Option names

        /// <summary>
        /// Option used to specify server url
        /// </summary>
        private const string OPTION_SERVER = "send-mail-url";
        /// <summary>
        /// Option used to specify server username
        /// </summary>
        private const string OPTION_USERNAME = "send-mail-username";
        /// <summary>
        /// Option used to specify server password
        /// </summary>
        private const string OPTION_PASSWORD = "send-mail-password";
        /// <summary>
        /// Option used to specify sender
        /// </summary>
        private const string OPTION_SENDER = "send-mail-from";
        /// <summary>
        /// Option used to specify recipient(s)
        /// </summary>
        private const string OPTION_RECIPIENT = "send-mail-to";
        /// <summary>
        /// Option used to specify mail subject
        /// </summary>
        private const string OPTION_SUBJECT = "send-mail-subject";
        /// <summary>
        /// Option used to specify mail body
        /// </summary>
        private const string OPTION_BODY = "send-mail-body";
        /// <summary>
        /// Option used to specify mail level
        /// </summary>
        private const string OPTION_SENDLEVEL = "send-mail-level";
        /// <summary>
        /// Option used to specify if reports are sent for other operations than backups
        /// </summary>
        private const string OPTION_SENDALL = "send-mail-any-operation";
        /// <summary>
        /// Option used to specify what format the result is sent in.
        /// </summary>
        private const string OPTION_RESULT_FORMAT = "send-mail-result-output-format";
        /// <summary>
        /// Option used to set the log level for mail reports
        /// </summary>
        private const string OPTION_LOG_LEVEL = "send-mail-log-level";
        /// <summary>
        /// Option used to set the log filters for mail reports
        /// </summary>
        private const string OPTION_LOG_FILTER = "send-mail-log-filter";
        /// <summary>
        /// Option used to set the maximum number of log lines
        /// </summary>
        private const string OPTION_MAX_LOG_LINES = "send-mail-max-log-lines";
        #endregion

        #region Option defaults
        /// <summary>
        /// The default mail sender
        /// </summary>
        private const string DEFAULT_SENDER = "no-reply";
        #endregion

        #region Private variables
        /// <summary>
        /// The server url to use
        /// </summary>
        private string m_server;
        /// <summary>
        /// The server username
        /// </summary>
        private string m_username;
        /// <summary>
        /// The server password
        /// </summary>
        private string m_password;
        /// <summary>
        /// The mail sender
        /// </summary>
        private string m_from;
        /// <summary>
        /// The mail recipient
        /// </summary>
        private string m_to;
        #endregion


        #region Implementation of IGenericModule
        /// <summary>
        /// The module key, used to activate or deactivate the module on the commandline
        /// </summary>
        public override string Key { get { return "sendmail"; } }

        /// <summary>
        /// A localized string describing the module with a friendly name
        /// </summary>
        public override string DisplayName { get { return Strings.SendMail.Displayname;} }

        /// <summary>
        /// A localized description of the module
        /// </summary>
        public override string Description { get { return Strings.SendMail.Description; } }

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
                    new CommandLineArgument(OPTION_RECIPIENT, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionRecipientShort, Strings.SendMail.OptionRecipientLong),
                    new CommandLineArgument(OPTION_SENDER, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionSenderShort, Strings.SendMail.OptionSenderLong, DEFAULT_SENDER),
                    new CommandLineArgument(OPTION_SUBJECT, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionSubjectShort, Strings.SendMail.OptionSubjectLong(OPTION_BODY), DEFAULT_SUBJECT),
                    new CommandLineArgument(OPTION_BODY, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionBodyShort, Strings.SendMail.OptionBodyLong, DEFAULT_BODY),
                    new CommandLineArgument(OPTION_SERVER, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionServerShort, Strings.SendMail.OptionServerLong),
                    new CommandLineArgument(OPTION_USERNAME, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionUsernameShort, Strings.SendMail.OptionUsernameLong),
                    new CommandLineArgument(OPTION_PASSWORD, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionPasswordShort, Strings.SendMail.OptionPasswordLong),
                    new CommandLineArgument(OPTION_SENDLEVEL, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionSendlevelShort, Strings.SendMail.OptionSendlevelLong(ParsedResultType.Success.ToString(), ParsedResultType.Warning.ToString(), ParsedResultType.Error.ToString(), ParsedResultType.Fatal.ToString(), "All"), DEFAULT_LEVEL, null, Enum.GetNames(typeof(ParsedResultType)).Union(new string [] { "All" }).ToArray()),
                    new CommandLineArgument(OPTION_SENDALL, CommandLineArgument.ArgumentType.Boolean, Strings.SendHttpMessage.SendhttpanyoperationShort, Strings.SendHttpMessage.SendhttpanyoperationLong),

                    new CommandLineArgument(OPTION_LOG_LEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.OptionLoglevellShort, Strings.ReportHelper.OptionLoglevelLong, DEFAULT_LOG_LEVEL.ToString(), null, Enum.GetNames(typeof(Logging.LogMessageType))),
                    new CommandLineArgument(OPTION_LOG_FILTER, CommandLineArgument.ArgumentType.String, Strings.ReportHelper.OptionLogfilterShort, Strings.ReportHelper.OptionLogfilterLong),
                    new CommandLineArgument(OPTION_MAX_LOG_LINES, CommandLineArgument.ArgumentType.Integer, Strings.ReportHelper.OptionmaxloglinesShort, Strings.ReportHelper.OptionmaxloglinesLong, DEFAULT_LOGLINES.ToString()),

                    new CommandLineArgument(OPTION_RESULT_FORMAT, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.ResultFormatShort, Strings.ReportHelper.ResultFormatLong(Enum.GetNames(typeof(ResultExportFormat))), DEFAULT_EXPORT_FORMAT.ToString(), null, Enum.GetNames(typeof(ResultExportFormat))),
                });
            }
        }

        protected override string SubjectOptionName => OPTION_SUBJECT;
        protected override string BodyOptionName => OPTION_BODY;
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

            commandlineOptions.TryGetValue(OPTION_SERVER, out m_server);
            commandlineOptions.TryGetValue(OPTION_USERNAME, out m_username);
            commandlineOptions.TryGetValue(OPTION_PASSWORD, out m_password);
            commandlineOptions.TryGetValue(OPTION_SENDER, out m_from);

            if (string.IsNullOrEmpty(m_from))
                m_from = DEFAULT_SENDER;

            return true;
        }

        #endregion

        #region Implementation of IGenericCallbackModule

        /// <summary>
        /// Sends the email message
        /// </summary>
        /// <param name="subject">The subject line.</param>
        /// <param name="body">The message body.</param>
        protected override void SendMessage(string subject, string body)
        {
            var message = new MimeMessage();
            MailboxAddress mailbox;
            foreach (string s in m_to.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries))
                if (MailboxAddress.TryParse(s.Replace("\"", ""), out mailbox))
                    message.To.Add(mailbox);

            var mailboxToFirst = (MailboxAddress)message.To.First();
            string toMailDomain = mailboxToFirst.Address.Substring(mailboxToFirst.Address.LastIndexOf("@", StringComparison.Ordinal) + 1);

            string from = m_from.Trim().Replace("\"", "");
            if (from.IndexOf('@') < 0)
            {
                if (from.EndsWith(">", StringComparison.Ordinal))
                    from = from.Insert(from.Length - 1, "@" + toMailDomain);
                else
                    from = string.Format("No Reply - Backup report <{0}@{1}>", from, toMailDomain);
            }

            if (MailboxAddress.TryParse(from, out mailbox))
                message.From.Add(mailbox);

            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body, ContentTransferEncoding = ContentEncoding.EightBit };

            List<string> servers = null;
            if (string.IsNullOrEmpty(m_server))
            {
                var dnsclient = new LookupClient();
                var records = dnsclient.Query(toMailDomain, QueryType.MX).Answers.MxRecords();

                servers = records.OrderBy(record => record.Preference).Select(x => "smtp://" + x.Exchange).Distinct().ToList();
                if (servers.Count == 0)
                    throw new IOException(Strings.SendMail.FailedToLookupMXServer(OPTION_SERVER));
            }
            else
            {
                servers = (from n in m_server.Split(new string[] { ";" }, StringSplitOptions.RemoveEmptyEntries)
                           let srv = (n == null || n.IndexOf("://", StringComparison.OrdinalIgnoreCase) > 0) ? n : "smtp://" + n
                           where !string.IsNullOrEmpty(srv)
                           select srv).Distinct().ToList();
            }

            Exception lastEx = null;
            string lastServer = null;

            foreach (var server in servers)
            {
                if (lastEx != null)
                    Logging.Log.WriteWarningMessage(LOGTAG, "SendMailFailedWillRetry", lastEx, Strings.SendMail.SendMailFailedRetryError(lastServer, lastEx.Message, server));

                lastServer = server;
                try
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        try
                        {
                            using (var client = new SmtpClient(new MailKit.ProtocolLogger(ms)))
                            {
                                client.Timeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;

                                // Backward compatibility fix for setup prior to using MailKit
                                var uri = new System.Uri(server);
                                if (uri.Scheme.ToLowerInvariant() == "tls")
                                    uri = new System.Uri("smtp://" + uri.Host + ":" + (uri.Port <= 0 ? 587 : uri.Port) + "/?starttls=always");

                                client.Connect(uri);

                                if (!string.IsNullOrEmpty(m_username) && !string.IsNullOrEmpty(m_password))
                                    client.Authenticate(m_username, m_password);

                                client.Send(message);
                                client.Disconnect(true);
                            }
                        }
                        finally
                        {
                            var log = Encoding.UTF8.GetString(ms.GetBuffer());
                            if (!string.IsNullOrWhiteSpace(log))
                                Logging.Log.WriteProfilingMessage(LOGTAG, "SendMailResult", Strings.SendMail.SendMailLog(log));
                        }
                    }

                    lastEx = null;
                    Logging.Log.WriteInformationMessage(LOGTAG, "SendMailComplete", Strings.SendMail.SendMailSuccess(server));
                    break;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (lastEx != null)
                throw lastEx;
        }

        #endregion
    }
}
