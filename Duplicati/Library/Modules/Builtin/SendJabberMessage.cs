using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using DnsLib;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using System.Net.NetworkInformation;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Modules.Builtin
{
    public class SendJabberMessage : Interface.IGenericCallbackModule
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

        #endregion

        #region Option defaults
        /// <summary>
        /// The default mail level
        /// </summary>
        private const ReportLevels DEFAULT_LEVEL = ReportLevels.All;
        /// <summary>
        /// The default mail body
        /// </summary>
        private readonly string DEFAULT_MESSAGE = string.Format("Duplicati %OPERATIONNAME% report for %backup-name%{0}{0}%RESULT%", Environment.NewLine);
        /// <summary>
        /// The default mail sender
        /// </summary>
        private const string DEFAULT_SENDER = "no-reply";
        #endregion

        /// <summary>
        /// The allowed mail levels
        /// </summary>
        [Flags]
        private enum ReportLevels
        {
            Success = 0x1,
            Warning = 0x2,
            Error = 0x4,
            All = Success | Warning | Error
        }

        #region Private variables

        /// <summary>
        /// The cached name of the operation
        /// </summary>
        private string m_operationname;
        /// <summary>
        /// The cached remote url
        /// </summary>
        private string m_remoteurl;
        /// <summary>
        /// The cached local path
        /// </summary>
        private string[] m_localpath;
        /// <summary>
        /// The cached set of options
        /// </summary>
        private IDictionary<string, string> m_options; 

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
        /// <summary>
        /// The XMPP message
        /// </summary>
        private string m_body;
        /// <summary>
        /// The XMPP send level
        /// </summary>
        private ReportLevels m_level;
        /// <summary>
        /// True to send all operations
        /// </summary>
        private bool m_sendAll;

        #endregion


        #region Implementation of IGenericModule

        /// <summary>
        /// The module key, used to activate or deactivate the module on the commandline
        /// </summary>
        public string Key { get { return "sendxmpp"; } }

        /// <summary>
        /// A localized string describing the module with a friendly name
        /// </summary>
        public string DisplayName { get { return LC.L("XMPP report module");} }

        /// <summary>
        /// A localized description of the module
        /// </summary>
        public string Description { get { return LC.L("This module provides support for sending status reports via XMPP messages"); } }

        /// <summary>
        /// A boolean value that indicates if the module should always be loaded.
        /// If true, the  user can choose to not load the module by entering the appropriate commandline option.
        /// If false, the user can choose to load the module by entering the appropriate commandline option.
        /// </summary>
        public bool LoadAsDefault { get { return true; } }

        /// <summary>
        /// Gets a list of supported commandline arguments
        /// </summary>
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(OPTION_RECIPIENT, CommandLineArgument.ArgumentType.String, LC.L("XMPP recipient email"), LC.L("The users who should have the messages sent, specify multiple users seperated with commas")),
                    new CommandLineArgument(OPTION_MESSAGE, CommandLineArgument.ArgumentType.String, LC.L("The message template"), LC.L("This value can be a filename. If a the file exists, the file contents will be used as the message.\n\nIn the message, certain tokens are replaced:\n%OPERATIONNAME% - The name of the operation, normally \"Backup\"\n%REMOTEURL% - Remote server url\n%LOCALPATH% - The path to the local files or folders involved in the operation (if any)\n\nAll commandline options are also reported within %value%, e.g. %volsize%. Any unknown/unset value is removed."), DEFAULT_MESSAGE),
                    new CommandLineArgument(OPTION_USERNAME, CommandLineArgument.ArgumentType.String, LC.L("The XMPP username"), LC.L("The username for the account that will send the message, including the hostname. I.e. \"account@jabber.org/Home\"")),
                    new CommandLineArgument(OPTION_PASSWORD, CommandLineArgument.ArgumentType.String, LC.L("The XMPP password"), LC.L("The password for the account that will send the message")),
                    new CommandLineArgument(OPTION_SENDLEVEL, CommandLineArgument.ArgumentType.Enumeration, LC.L("The messages to send"), LC.L("You can specify one of \"{0}\", \"{1}\", \"{2}\". \nYou can supply multiple options with a comma seperator, e.g. \"{0},{1}\". The special value \"{3}\" is a shorthand for \"{0},{1},{2}\" and will cause all backup operations to send a message.", ReportLevels.Success, ReportLevels.Warning, ReportLevels.Error, ReportLevels.All), DEFAULT_LEVEL.ToString(), null, Enum.GetNames(typeof(ReportLevels))),
                    new CommandLineArgument(OPTION_SENDALL, CommandLineArgument.ArgumentType.Boolean, LC.L("Send messages for all operations"), LC.L("By default, messages will only be sent after a Backup operation. Use this option to send messages for all operations")),
                });
            }
        }

        /// <summary>
        /// This method is the interception where the module can interact with the execution environment and modify the settings.
        /// </summary>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            //We need at least a recipient
            commandlineOptions.TryGetValue(OPTION_RECIPIENT, out m_to);
            if (string.IsNullOrEmpty(m_to))
                return;

            commandlineOptions.TryGetValue(OPTION_USERNAME, out m_username);
            commandlineOptions.TryGetValue(OPTION_PASSWORD, out m_password);
            commandlineOptions.TryGetValue(OPTION_MESSAGE, out m_body);
            m_options = commandlineOptions;

            m_level = 0;

            string tmp;
            commandlineOptions.TryGetValue(OPTION_SENDLEVEL, out tmp);
            if (!string.IsNullOrEmpty(tmp))
                foreach(var s in tmp.Split(new string[] {","}, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.IsNullOrEmpty(s))
                        continue;

                    ReportLevels m;
                    if (Enum.TryParse(s.Trim(), true, out m))
                        m_level |= m;
                }

            if (m_level == 0)
                m_level = DEFAULT_LEVEL;

            m_sendAll = Utility.Utility.ParseBoolOption(commandlineOptions, OPTION_SENDALL);

            if (string.IsNullOrEmpty(m_body))
                m_body = DEFAULT_MESSAGE;
        }

        #endregion

        #region Implementation of IGenericCallbackModule

        /// <summary>
        /// Called when the operation starts
        /// </summary>
        /// <param name="operationname">The full name of the operation</param>
        /// <param name="remoteurl">The remote backend url</param>
        /// <param name="localpath">The local path, if required</param>
        public void OnStart(string operationname, ref string remoteurl, ref string[] localpath)
        {
            m_operationname = operationname;
            m_remoteurl = remoteurl;
            m_localpath = localpath;
        }

        /// <summary>
        /// Called when the operation finishes
        /// </summary>
        /// <param name="result">The result object, if this derives from an exception, the operation failed</param>
        public void OnFinish(object result)
        {
            //If no email is supplied, then skip
            if (string.IsNullOrEmpty(m_to))
                return;

            //If we do not report this action, then skip
            if (!m_sendAll && !string.Equals(m_operationname, "Backup", StringComparison.InvariantCultureIgnoreCase))
                return;

            if (string.Equals(m_operationname, "Backup", StringComparison.InvariantCultureIgnoreCase))
            {
                if (m_level != ReportLevels.All)
                {
                    ReportLevels level;
                    if (result is Exception)
                        level = ReportLevels.Error;
                    else if (result != null && result is Library.Interface.IBackupResults && (result as Library.Interface.IBackupResults).Errors.Count() > 0)
                        level = ReportLevels.Warning;
                    else
                        level = ReportLevels.Success;

                    //Check if this level should send mail
                    if ((m_level & level) == 0)
                        return;
                }
            }

            try
            {
                string body = m_body;
                if (body != DEFAULT_MESSAGE && System.IO.File.Exists(body))
                    body = System.IO.File.ReadAllText(body);

                body = ReplaceTemplate(body, result);

                SendMessages(body);
            }
            catch (Exception ex)
            {
                Exception top = ex;
                var sb = new StringBuilder();
                while (top != null)
                {
                    if (sb.Length != 0)
                        sb.Append("--> ");
                    sb.AppendFormat("{0}: {1}{2}", top.GetType().FullName, top.Message, Environment.NewLine);
                    top = top.InnerException;
                }

                Logging.Log.WriteMessage(LC.L("Failed to send jabber message: {0}", sb.ToString()), LogMessageType.Warning, ex);
            }
        }

        #endregion

        private void SendMessages(string message)
        {
            Exception ex = null;
            var waitEvent = new System.Threading.ManualResetEvent(false);

            var uri = new Library.Utility.Uri(m_username.Contains("://") ? m_username : "http://" + m_username);
            var con = new agsXMPP.XmppClientConnection(uri.Host, uri.Port == -1 ? (uri.Scheme == "https" ? 5223 :5222) : uri.Port);
            if (uri.Scheme == "https")
                con.UseSSL = true;

            var resource = uri.Path ?? "";
            if (resource.StartsWith("/"))
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
                ex = new Exception(string.Format("Failed to log in: {0}", e.ToString()));
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
                throw new TimeoutException(LC.L("Timeout occured while logging in to jabber server"));
        }

        private string ReplaceTemplate(string input, object result)
        {
            input = Regex.Replace(input, "\\%OPERATIONNAME\\%", m_operationname ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            input = Regex.Replace(input, "\\%REMOTEURL\\%", m_remoteurl ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            input = Regex.Replace(input, "\\%LOCALPATH\\%", m_localpath == null ? "" : string.Join(System.IO.Path.PathSeparator.ToString(), m_localpath), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (input.IndexOf("%RESULT%", StringComparison.InvariantCultureIgnoreCase) >= 0)
                using (TempFile tf = new TempFile())
                {
                    RunScript.SerializeResult(tf, result);
                    input = Regex.Replace(input, "\\%RESULT\\%", System.IO.File.ReadAllText(tf), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }

            foreach (KeyValuePair<string, string> kv in m_options)
                input = Regex.Replace(input, "\\%" + kv.Key + "\\%", kv.Value ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!m_options.ContainsKey("backup-name"))
                input = Regex.Replace(input, "\\%backup-name\\%", System.IO.Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location) ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            input = Regex.Replace(input, "\\%[^\\%]+\\%", "");
            return input;
        }

    }
}
