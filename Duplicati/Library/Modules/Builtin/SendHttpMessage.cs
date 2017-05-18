using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Duplicati.Library.Modules.Builtin {
    public class SendHttpMessage : Interface.IGenericCallbackModule
    {
        #region Option names

        /// <summary>
        /// Option used to specify server URL
        /// </summary>
        private const string OPTION_URL = "send-http-url";
        /// <summary>
        /// Option used to specify report body
        /// </summary>
        private const string OPTION_MESSAGE = "send-http-message";
        /// <summary>
        /// Option used to specify the parameter name for the message
        /// </summary>
        private const string OPTION_MESSAGE_PARAMETER_NAME = "send-http-message-parameter-name";
        /// <summary>
        /// Option used to specify extra parameters
        /// </summary>
        private const string OPTION_EXTRA_PARAMETERS = "send-http-extra-parameters";
        /// <summary>
        /// Option used to specify report level
        /// </summary>
        private const string OPTION_SENDLEVEL = "send-http-level";
        /// <summary>
        /// Option used to specify if reports are sent for other operations than backups
        /// </summary>
        private const string OPTION_SENDALL = "send-http-any-operation";

        #endregion

        #region Option defaults
        /// <summary>
        /// The default message parameter name
        /// </summary>
        private const string DEFAULT_MESSAGE_PARAMETER_NAME = "message";
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
        /// The HTTP report URL
        /// </summary>
        private string m_url;
        /// <summary>
        /// The message parameter name
        /// </summary>
        private string m_messageParameterName;
        /// <summary>
        /// The message parameter name
        /// </summary>
        private string m_extraParameters;
        /// <summary>
        /// The HTTP message
        /// </summary>
        private string m_body;
        /// <summary>
        /// The HTTP send level
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
        public string Key { get { return "sendhttp"; } }

        /// <summary>
        /// A localized string describing the module with a friendly name
        /// </summary>
        public string DisplayName { get { return Strings.SendHttpMessage.DisplayName;} }

        /// <summary>
        /// A localized description of the module
        /// </summary>
        public string Description { get { return Strings.SendHttpMessage.Description; } }

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
                    new CommandLineArgument(OPTION_URL, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpurlShort, Strings.SendHttpMessage.SendhttpurlLong),
                    new CommandLineArgument(OPTION_MESSAGE, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpmessageShort, Strings.SendHttpMessage.SendhttpmessageLong, DEFAULT_MESSAGE),
                    new CommandLineArgument(OPTION_MESSAGE_PARAMETER_NAME, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpmessageparameternameShort, Strings.SendHttpMessage.SendhttpmessageparameternameLong, DEFAULT_MESSAGE_PARAMETER_NAME),
                    new CommandLineArgument(OPTION_EXTRA_PARAMETERS, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpextraparametersShort, Strings.SendHttpMessage.SendhttpextraparametersLong),
                    new CommandLineArgument(OPTION_SENDLEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.SendHttpMessage.SendhttplevelShort, Strings.SendHttpMessage.SendhttplevelLong(ParsedResultType.Success.ToString(), ParsedResultType.Warning.ToString(), ParsedResultType.Error.ToString(), ParsedResultType.Fatal.ToString(), "All"), DEFAULT_LEVEL, null, Enum.GetNames(typeof(ParsedResultType)).Union(new string[] { "All" } ).ToArray()),
                    new CommandLineArgument(OPTION_SENDALL, CommandLineArgument.ArgumentType.Boolean, Strings.SendHttpMessage.SendhttpanyoperationShort, Strings.SendHttpMessage.SendhttpanyoperationLong)
                });
            }
        }

        /// <summary>
        /// This method is the interception where the module can interact with the execution environment and modify the settings.
        /// </summary>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            //We need a URL to report to
            commandlineOptions.TryGetValue(OPTION_URL, out m_url);
            if (string.IsNullOrEmpty(m_url))
                return;

            commandlineOptions.TryGetValue(OPTION_MESSAGE_PARAMETER_NAME, out m_messageParameterName);
            if (string.IsNullOrEmpty(m_messageParameterName))
                m_messageParameterName = DEFAULT_MESSAGE_PARAMETER_NAME;

            commandlineOptions.TryGetValue(OPTION_EXTRA_PARAMETERS, out m_extraParameters);

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
            //If no URL is supplied, then skip
            if (string.IsNullOrEmpty(m_url))
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

                Logging.Log.WriteMessage(Strings.SendHttpMessage.SendMessageError(sb.ToString()), LogMessageType.Warning, ex);
            }
        }

        #endregion

        private void SendMessages(string message) {
            Exception ex = null;

            var request = (HttpWebRequest)WebRequest.Create(m_url);

            var postData = $"{m_messageParameterName}={System.Uri.EscapeDataString(message)}";
            if (!string.IsNullOrEmpty(m_extraParameters)) 
            {
                postData += $"&{System.Uri.EscapeUriString(m_extraParameters)}";
            }
            var data = Encoding.UTF8.GetBytes(postData);

            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = data.Length;

            try 
            {
                using (var stream = request.GetRequestStream()) 
                {
                    stream.Write(data, 0, data.Length);
                }

                var response = (HttpWebResponse)request.GetResponse();
                var responseString = new StreamReader(response.GetResponseStream()).ReadToEnd();
            }
            catch (Exception e) 
            {
                ex = e;
            }

            if (ex != null)
                throw ex;
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
