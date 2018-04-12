using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Modules.Builtin.ResultSerialization;
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Duplicati.Library.Modules.Builtin {
    public class SendHttpMessage : ReportHelper
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<SendHttpMessage>();

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
        /// Option used to specify report verb
        /// </summary>
        private const string OPTION_VERB = "send-http-verb";
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
        /// <summary>
        /// Option used to specify what format the result is sent in.
        /// </summary>
        private const string OPTION_RESULT_FORMAT = "send-http-result-output-format";
        /// <summary>
        /// Option used to set the log level
        /// </summary>
        private const string OPTION_LOG_LEVEL = "send-http-log-level";
        /// <summary>
        /// Option used to set the log level
        /// </summary>
        private const string OPTION_LOG_FILTER = "send-http-log-filter";
        /// <summary>
        /// Option used to set the maximum number of log lines
        /// </summary>
        private const string OPTION_MAX_LOG_LINES = "send-http-max-log-lines";

        #endregion

        #region Option defaults
        /// <summary>
        /// The default message parameter name
        /// </summary>
        private const string DEFAULT_MESSAGE_PARAMETER_NAME = "message";
        /// <summary>
        /// The default message body
        /// </summary>
        protected override string DEFAULT_BODY => string.Format("Duplicati %OPERATIONNAME% report for %backup-name%{0}{0}%RESULT%", Environment.NewLine);
        /// <summary>
        /// Don't use the subject for HTTP
        /// </summary>
        protected override string DEFAULT_SUBJECT => string.Empty;
        #endregion

        #region Private variables
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
        /// The http verb
        /// </summary>
        private string m_verb;

        #endregion


        #region Implementation of IGenericModule

        /// <summary>
        /// The module key, used to activate or deactivate the module on the commandline
        /// </summary>
        public override string Key { get { return "sendhttp"; } }

        /// <summary>
        /// A localized string describing the module with a friendly name
        /// </summary>
        public override string DisplayName { get { return Strings.SendHttpMessage.DisplayName;} }

        /// <summary>
        /// A localized description of the module
        /// </summary>
        public override string Description { get { return Strings.SendHttpMessage.Description; } }

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
                    new CommandLineArgument(OPTION_URL, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpurlShort, Strings.SendHttpMessage.SendhttpurlLong),
                    new CommandLineArgument(OPTION_MESSAGE, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpmessageShort, Strings.SendHttpMessage.SendhttpmessageLong, DEFAULT_BODY),
                    new CommandLineArgument(OPTION_MESSAGE_PARAMETER_NAME, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpmessageparameternameShort, Strings.SendHttpMessage.SendhttpmessageparameternameLong, DEFAULT_MESSAGE_PARAMETER_NAME),
                    new CommandLineArgument(OPTION_EXTRA_PARAMETERS, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpextraparametersShort, Strings.SendHttpMessage.SendhttpextraparametersLong),
                    new CommandLineArgument(OPTION_SENDLEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.SendHttpMessage.SendhttplevelShort, Strings.SendHttpMessage.SendhttplevelLong(ParsedResultType.Success.ToString(), ParsedResultType.Warning.ToString(), ParsedResultType.Error.ToString(), ParsedResultType.Fatal.ToString(), "All"), DEFAULT_LEVEL, null, Enum.GetNames(typeof(ParsedResultType)).Union(new string[] { "All" } ).ToArray()),
                    new CommandLineArgument(OPTION_SENDALL, CommandLineArgument.ArgumentType.Boolean, Strings.SendHttpMessage.SendhttpanyoperationShort, Strings.SendHttpMessage.SendhttpanyoperationLong),

                    new CommandLineArgument(OPTION_VERB, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.HttpverbShort, Strings.SendHttpMessage.HttpverbLong, "POST"),
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
            //We need a URL to report to
            commandlineOptions.TryGetValue(OPTION_URL, out m_url);
            if (string.IsNullOrEmpty(m_url))
                return false;

            commandlineOptions.TryGetValue(OPTION_MESSAGE_PARAMETER_NAME, out m_messageParameterName);
            if (string.IsNullOrEmpty(m_messageParameterName))
                m_messageParameterName = DEFAULT_MESSAGE_PARAMETER_NAME;

            commandlineOptions.TryGetValue(OPTION_EXTRA_PARAMETERS, out m_extraParameters);
            commandlineOptions.TryGetValue(OPTION_VERB, out m_verb);
            if (string.IsNullOrWhiteSpace(m_verb))
                m_verb = "POST";

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

        protected override void SendMessage(string subject, string body) {
            Exception ex = null;

            byte[] data;
            string contenttype;

            if (ExportFormat == ResultExportFormat.Json)
            {
                contenttype = "application/json";
                data = Encoding.UTF8.GetBytes(body);
            }
            else
            {
                contenttype = "application/x-www-form-urlencoded";
                var postData = $"{m_messageParameterName}={System.Uri.EscapeDataString(body)}";
                if (!string.IsNullOrEmpty(m_extraParameters))
                {
                    postData += $"&{System.Uri.EscapeUriString(m_extraParameters)}";
                }
                data = Encoding.UTF8.GetBytes(postData);
            }


            var request = (HttpWebRequest)WebRequest.Create(m_url);
            request.ContentType = contenttype;
            request.Method = m_verb;
            request.ContentLength = data.Length;

            try 
            {
                using (var stream = request.GetRequestStream()) 
                {
                    stream.Write(data, 0, data.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, 
                                                    "HttpResponseMessage", 
                                                     "HTTP Response: {0} - {1}: {2}", 
                                                     ((int)response.StatusCode).ToString(),
                                                     response.StatusDescription,
                                                     new StreamReader(response.GetResponseStream()).ReadToEnd()
                                                    );
                }
            }
            catch (Exception e) 
            {
                ex = e;
                if (ex is WebException && ((WebException)ex).Response is HttpWebResponse)
                {
                    var response = ((WebException)ex).Response as HttpWebResponse;

                    Logging.Log.WriteWarningMessage(LOGTAG, 
                                                    "HttpResponseError",
                                                    ex,
                                                     "HTTP Response: {0} - {1}: {2}",
                                                     ((int)response.StatusCode).ToString(),
                                                     response.StatusDescription,
                                                     new StreamReader(response.GetResponseStream()).ReadToEnd()
                                                    );
                }
            }

            if (ex != null)
                throw ex;
        }
    }
}
