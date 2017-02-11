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
        private const string DEFAULT_LEVEL = "all";
        /// <summary>
        /// The default message body
        /// </summary>
        private readonly string DEFAULT_MESSAGE = string.Format("Duplicati %OPERATIONNAME% report for %backup-name%{0}{0}%RESULT%", Environment.NewLine);
        #endregion

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
        /// The parsed result level if the operation is a backup, empty otherwise
        /// </summary>
        private string m_parsedresultlevel = string.Empty;

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
        private string[] m_levels;
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
        public string DisplayName { get { return Strings.SendJabberMessage.DisplayName;} }

        /// <summary>
        /// A localized description of the module
        /// </summary>
        public string Description { get { return Strings.SendJabberMessage.Description; } }

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
                    new CommandLineArgument(OPTION_RECIPIENT, CommandLineArgument.ArgumentType.String, Strings.SendJabberMessage.SendxmpptoShort, Strings.SendJabberMessage.SendxmpptoLong),
                    new CommandLineArgument(OPTION_MESSAGE, CommandLineArgument.ArgumentType.String, Strings.SendJabberMessage.SendxmppmessageShort, Strings.SendJabberMessage.SendxmppmessageLong, DEFAULT_MESSAGE),
                    new CommandLineArgument(OPTION_USERNAME, CommandLineArgument.ArgumentType.String, Strings.SendJabberMessage.SendxmppusernameShort, Strings.SendJabberMessage.SendxmppusernameLong),
                    new CommandLineArgument(OPTION_PASSWORD, CommandLineArgument.ArgumentType.String, Strings.SendJabberMessage.SendxmpppasswordShort, Strings.SendJabberMessage.SendxmpppasswordLong),
                    new CommandLineArgument(OPTION_SENDLEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.SendJabberMessage.SendxmpplevelShort, Strings.SendJabberMessage.SendxmpplevelLong(ParsedResultType.Success.ToString(), ParsedResultType.Warning.ToString(), ParsedResultType.Error.ToString(), ParsedResultType.Fatal.ToString(), "All"), DEFAULT_LEVEL, null, Enum.GetNames(typeof(ParsedResultType)).Union(new string[] { "All" } ).ToArray()),
                    new CommandLineArgument(OPTION_SENDALL, CommandLineArgument.ArgumentType.Boolean, Strings.SendJabberMessage.SendxmppanyoperationShort, Strings.SendJabberMessage.SendxmppanyoperationLong),
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

            string tmp;
            commandlineOptions.TryGetValue(OPTION_SENDLEVEL, out tmp);
            if (!string.IsNullOrEmpty(tmp))
                m_levels =
                    tmp
                    .Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToArray();

            if (m_levels == null || m_levels.Length == 0)
                m_levels =
                    DEFAULT_LEVEL
                    .Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToArray();

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
                ParsedResultType level;
                if (result is Exception)
                    level = ParsedResultType.Fatal;
                else if (result != null && result is Library.Interface.IBasicResults)
                    level = ((IBasicResults)result).ParsedResult;
                else
                    level = ParsedResultType.Error;

                m_parsedresultlevel = level.ToString();

                if (!m_levels.Any(x => string.Equals(x, "all", StringComparison.OrdinalIgnoreCase)))
                {
                    //Check if this level should send mail
                    if (!m_levels.Any(x => string.Equals(x, level.ToString(), StringComparison.OrdinalIgnoreCase)))
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

                Logging.Log.WriteMessage(Strings.SendJabberMessage.SendMessageError(sb.ToString()), LogMessageType.Warning, ex);
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
                throw new TimeoutException(Strings.SendJabberMessage.LoginTimeoutError);
        }

        private string ReplaceTemplate(string input, object result)
        {
            input = Regex.Replace(input, "\\%OPERATIONNAME\\%", m_operationname ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            input = Regex.Replace(input, "\\%REMOTEURL\\%", m_remoteurl ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            input = Regex.Replace(input, "\\%LOCALPATH\\%", m_localpath == null ? "" : string.Join(System.IO.Path.PathSeparator.ToString(), m_localpath), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            input = Regex.Replace(input, "\\%PARSEDRESULT\\%", m_parsedresultlevel ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (input.IndexOf("%RESULT%", StringComparison.InvariantCultureIgnoreCase) >= 0)
                using (TempFile tf = new TempFile())
                {
                    RunScript.SerializeResult(tf, result);
                    input = Regex.Replace(input, "\\%RESULT\\%", System.IO.File.ReadAllText(tf), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }

            foreach (KeyValuePair<string, string> kv in m_options)
                input = Regex.Replace(input, "\\%" + kv.Key + "\\%", kv.Value ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!m_options.ContainsKey("backup-name"))
                input = Regex.Replace(input, "\\%backup-name\\%", System.IO.Path.GetFileNameWithoutExtension(Duplicati.Library.Utility.Utility.getEntryAssembly().Location) ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            input = Regex.Replace(input, "\\%[^\\%]+\\%", "");
            return input;
        }

    }
}
