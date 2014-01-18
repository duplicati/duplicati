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
    public class SendMail : Interface.IGenericCallbackModule
    {
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

        #endregion

        #region Option defaults
        /// <summary>
        /// The default subject
        /// </summary>
        private const string DEFAULT_SUBJECT = "Duplicati %OPERATIONNAME% report for %backup-name%";
        /// <summary>
        /// The default mail level
        /// </summary>
        private const MailLevels DEFAULT_LEVEL = MailLevels.All;
        /// <summary>
        /// The default mail body
        /// </summary>
        private const string DEFAULT_BODY = "%RESULT%";
        /// <summary>
        /// The default mail sender
        /// </summary>
        private const string DEFAULT_SENDER = "no-reply";
        #endregion

        /// <summary>
        /// The allowed mail levels
        /// </summary>
        [Flags]
        private enum MailLevels
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
        /// <summary>
        /// The mail subject
        /// </summary>
        private string m_subject;
        /// <summary>
        /// The mail body
        /// </summary>
        private string m_body;
        /// <summary>
        /// The mail send level
        /// </summary>
        private MailLevels m_level;
        /// <summary>
        /// True to send all operations
        /// </summary>
        private bool m_sendAll;

        #endregion


        #region Implementation of IGenericModule

        /// <summary>
        /// The module key, used to activate or deactivate the module on the commandline
        /// </summary>
        public string Key { get { return "sendmail"; } }

        /// <summary>
        /// A localized string describing the module with a friendly name
        /// </summary>
        public string DisplayName { get { return Strings.SendMail.Displayname;} }

        /// <summary>
        /// A localized description of the module
        /// </summary>
        public string Description { get { return Strings.SendMail.Description; } }

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
                    new CommandLineArgument(OPTION_RECIPIENT, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionRecipientShort, Strings.SendMail.OptionRecipientLong),
                    new CommandLineArgument(OPTION_SENDER, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionSenderShort, Strings.SendMail.OptionSenderLong, DEFAULT_SENDER),
                    new CommandLineArgument(OPTION_SUBJECT, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionSubjectShort, string.Format(Strings.SendMail.OptionSubjectLong, DEFAULT_SUBJECT, OPTION_BODY)),
                    new CommandLineArgument(OPTION_BODY, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionBodyShort, Strings.SendMail.OptionBodyLong, DEFAULT_BODY),
                    new CommandLineArgument(OPTION_SERVER, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionServerShort, Strings.SendMail.OptionServerLong),
                    new CommandLineArgument(OPTION_USERNAME, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionUsernameShort, Strings.SendMail.OptionUsernameLong),
                    new CommandLineArgument(OPTION_PASSWORD, CommandLineArgument.ArgumentType.String, Strings.SendMail.OptionPasswordShort, Strings.SendMail.OptionPasswordLong),
                    new CommandLineArgument(OPTION_SENDLEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.SendMail.OptionSendlevelShort, string.Format(Strings.SendMail.OptionSendlevelLong, MailLevels.Success, MailLevels.Warning, MailLevels.Error, MailLevels.All), DEFAULT_LEVEL.ToString(), null, Enum.GetNames(typeof(MailLevels))),
                    new CommandLineArgument(OPTION_SENDALL, CommandLineArgument.ArgumentType.Boolean, Strings.SendMail.OptionSendallShort, Strings.SendMail.OptionSendallLong),
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

            commandlineOptions.TryGetValue(OPTION_SERVER, out m_server);
            commandlineOptions.TryGetValue(OPTION_USERNAME, out m_username);
            commandlineOptions.TryGetValue(OPTION_PASSWORD, out m_password);
            commandlineOptions.TryGetValue(OPTION_SENDER, out m_from);
            commandlineOptions.TryGetValue(OPTION_SUBJECT, out m_subject);
            commandlineOptions.TryGetValue(OPTION_BODY, out m_body);
            m_options = commandlineOptions;

            m_level = 0;

            string tmp;
            commandlineOptions.TryGetValue(OPTION_SENDLEVEL, out tmp);
            if (!string.IsNullOrEmpty(tmp))
                foreach(var s in tmp.Split(new string[] {","}, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.IsNullOrEmpty(s))
                        continue;

                    MailLevels m;
                    if (Enum.TryParse(s.Trim(), true, out m))
                        m_level |= m;
                }

            if (m_level == 0)
                m_level = DEFAULT_LEVEL;

            m_sendAll = Utility.Utility.ParseBoolOption(commandlineOptions, OPTION_SENDALL);

            if (string.IsNullOrEmpty(m_subject))
                m_subject = DEFAULT_SUBJECT;
            if (string.IsNullOrEmpty(m_body))
                m_body = DEFAULT_BODY;
            if (string.IsNullOrEmpty(m_from))
                m_from = DEFAULT_SENDER;
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
                if (m_level != MailLevels.All)
                {
                    MailLevels level;
                    if (result is Exception)
                        level = MailLevels.Error;
                    else if (result != null && result is Library.Interface.IBackupResults && (result as Library.Interface.IBackupResults).Errors.Count() > 0)
                        level = MailLevels.Warning;
                    else
                        level = MailLevels.Success;

                    //Check if this level should send mail
                    if ((m_level & level) == 0)
                        return;
                }
            }

            try
            {
                string body = m_body;
                string subject = m_subject;
                if (body != DEFAULT_BODY && System.IO.File.Exists(body))
                    body = System.IO.File.ReadAllText(body);

                body = ReplaceTemplate(body, result);
                subject = ReplaceTemplate(subject, result);

                var message = new MailMessage();
                foreach(string s in m_to.Split(new [] { "," }, StringSplitOptions.RemoveEmptyEntries))
                    message.To.Add(new MailAddress(s.Replace("\"", "")));

                string from = m_from.Trim();
                if (from.IndexOf('@') < 0)
                {
                    if (from.EndsWith(">"))
                        from = from.Insert(from.Length - 1, "@" + message.To[0].Host);
                    else
                        from = string.Format("No Reply - Backup report <{0}@{1}>", from, message.To[0].Host);
                }
                message.From = new MailAddress(from.Replace("\"", ""));

                message.Subject = subject;
                message.Body = body;
                message.BodyEncoding = message.SubjectEncoding = Encoding.UTF8;

                List<string> servers = null;
                if (string.IsNullOrEmpty(m_server))
                {
                    var dnslite = new DnsLib.DnsLite();
                    var dnslist = new List<string>();

                    //Grab all IPv4 adresses
                    foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                        try 
                        {
                            foreach (IPAddress dnsAdress in networkInterface.GetIPProperties().DnsAddresses)
                                if (dnsAdress.AddressFamily == AddressFamily.InterNetwork)
                                    dnslist.Add(dnsAdress.ToString());
                        }
                        catch { }
                    
                    dnslist = dnslist.Distinct().ToList();
                    
                    // If we have no DNS servers, try Google and OpenDNS
                    if (dnslist.Count == 0) 
                    {
                        // https://developers.google.com/speed/public-dns/
                        dnslist.Add("8.8.8.8");
                        dnslist.Add("8.8.4.4");
                        
                        //http://www.opendns.com/opendns-ip-addresses/
                        dnslist.Add("208.67.222.222");
                        dnslist.Add("208.67.220.220");
                    }
                    
                    var oldStyleList = new ArrayList();
                    foreach(var s in dnslist)
                        oldStyleList.Add(s);
                        
                    dnslite.setDnsServers(oldStyleList);

                    servers = dnslite.getMXRecords(message.To[0].Host).OfType<MXRecord>().OrderBy(record => record.preference).Select(x => "smtp://" +  x.exchange).Distinct().ToList();
                    if (servers.Count == 0)
                        throw new IOException(string.Format(Strings.SendMail.FailedToLookupMXServer, OPTION_SERVER));
                }
                else 
                {
                    servers = (from n in m_server.Split(new string[] { ";" }, StringSplitOptions.RemoveEmptyEntries)
                              let srv = (n == null || n.IndexOf("://", StringComparison.InvariantCultureIgnoreCase) > 0) ? n : "smtp://" + n
                              where !string.IsNullOrEmpty(srv)
                              select srv).Distinct().ToList();
                }
                
                Exception lastEx = null;
                string lastServer = null;

                foreach(var server in servers)
                {
                    if (lastEx != null)
                        Logging.Log.WriteMessage(string.Format(Strings.SendMail.SendMailFailedRetryError, lastServer, lastEx.Message, server), LogMessageType.Warning, lastEx);
                
                    lastServer = server;
                    try
                    {
                        var serverUri = new System.Uri(server);
                        var useTls = string.Equals(serverUri.Scheme, "smtptls", StringComparison.InvariantCultureIgnoreCase) || string.Equals(serverUri.Scheme, "tls", StringComparison.InvariantCultureIgnoreCase);
                        var port = serverUri.Port <= 0 ? (useTls ? 587 : 25) : serverUri.Port;
                        var client = new SmtpClient(serverUri.Host, port);
                        client.Timeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
                        if (!string.IsNullOrEmpty(m_username) && !string.IsNullOrEmpty(m_password))
                            client.Credentials = new NetworkCredential(m_username, m_password);
                        
                        //This ensures that you can override settings from an app.config file
                        if (useTls)
                            client.EnableSsl = true;
        
                        client.Send(message);
                        lastEx = null;
                        Logging.Log.WriteMessage(string.Format(Strings.SendMail.SendMailSuccess, server), LogMessageType.Information);
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

                Logging.Log.WriteMessage(string.Format(Strings.SendMail.SendMailFailedError, sb.ToString()), LogMessageType.Warning, ex);
            }
        }

        #endregion

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
