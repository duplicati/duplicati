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
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using Uri = System.Uri;
using System.Threading;

namespace Duplicati.Library.Modules.Builtin
{
    /// <summary>
    /// Helper module to send HTTP report messages
    /// </summary>
    public class SendHttpMessage : ReportHelper
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<SendHttpMessage>();

        /// <summary>
        /// Entry describing a request to be sent
        /// </summary>
        /// <param name="Url">The url to send to</param>
        /// <param name="Verb">The verb to use</param>
        /// <param name="Format">The format to send</param>
        private record SendRequestType(string Url, string Verb, ResultExportFormat Format);

        #region Option names

        /// <summary>
        /// Option used to specify server URL
        /// </summary>
        private const string OPTION_URL = "send-http-url";
        /// <summary>
        /// Option used to specify server URLs for sending text reports
        /// </summary>
        private const string OPTION_URL_FORM = "send-http-form-urls";
        /// <summary>
        /// Option used to specify server URLs for sending json reports
        /// </summary>
        private const string OPTION_URL_JSON = "send-http-json-urls";
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

        /// <summary>
        /// The option used to accept a specific SSL certificate hash
        /// </summary>
        private const string OPTION_ACCEPT_SPECIFIED_CERTIFICATE = "send-http-accept-specified-ssl-hash";
        /// <summary>
        /// The option used to accept any SSL certificate
        /// </summary>
        private const string OPTION_ACCEPT_ANY_CERTIFICATE = "send-http-accept-any-ssl-certificate";

        /// <summary>
        /// The option used to specify the number of retries for sending the HTTP request
        /// </summary>
        private const string OPTION_SEND_HTTP_RETRIES = "send-http-retries";
        /// <summary>
        /// The option used to specify the delay between retries for sending the HTTP request
        /// </summary>
        private const string OPTION_SEND_HTTP_RETRY_DELAY = "send-http-retry-delay";

        #endregion

        #region Option defaults
        /// <summary>
        /// The default message parameter name
        /// </summary>
        private const string DEFAULT_MESSAGE_PARAMETER_NAME = "message";
        /// <summary>
        /// The default message body
        /// </summary>
        protected override string DEFAULT_BODY => string.Format("Duplicati %OPERATIONNAME% report for %backup-name% (%machine-id%, %backup-id%, %machine-name%){0}{0}%RESULT%", Environment.NewLine);
        /// <summary>
        /// Don't use the subject for HTTP
        /// </summary>
        protected override string DEFAULT_SUBJECT => string.Empty;
        /// <summary>
        /// The default number of retries
        /// </summary>
        private const int DEFAULT_RETRIES = 3;
        /// <summary>
        /// The default delay between retries
        /// </summary>
        private const string DEFAULT_RETRY_DELAY = "1s";
        #endregion

        #region Private variables
        /// <summary>
        /// The HTTP text report URLs
        /// </summary>
        private List<SendRequestType> m_report_targets;
        /// <summary>
        /// The message parameter name
        /// </summary>
        private string m_messageParameterName;
        /// <summary>
        /// The message parameter name
        /// </summary>
        private string m_extraParameters;

        /// <summary>
        /// Option to accept any SSL certificate
        /// </summary>
        private bool m_acceptAnyCertificate;

        /// <summary>
        /// Specific hashes to be accepted by the certificate validator
        /// </summary>
        private string[] m_acceptSpecificCertificates;

        /// <summary>
        /// The number of retries to attempt
        /// </summary>
        private int m_retries;
        /// <summary>
        /// The delay between retries
        /// </summary>
        private TimeSpan m_retryDelay;

        #endregion


        #region Implementation of IGenericModule

        /// <summary>
        /// The module key, used to activate or deactivate the module on the commandline
        /// </summary>
        public override string Key { get { return "sendhttp"; } }

        /// <summary>
        /// A localized string describing the module with a friendly name
        /// </summary>
        public override string DisplayName { get { return Strings.SendHttpMessage.DisplayName; } }

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
        public override IList<ICommandLineArgument> SupportedCommands =>
        [
            new CommandLineArgument(OPTION_URL, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpurlShort, Strings.SendHttpMessage.SendhttpurlLong),
            new CommandLineArgument(OPTION_MESSAGE, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpmessageShort, Strings.SendHttpMessage.SendhttpmessageLong, DEFAULT_BODY),
            new CommandLineArgument(OPTION_MESSAGE_PARAMETER_NAME, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpmessageparameternameShort, Strings.SendHttpMessage.SendhttpmessageparameternameLong, DEFAULT_MESSAGE_PARAMETER_NAME),
            new CommandLineArgument(OPTION_EXTRA_PARAMETERS, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpextraparametersShort, Strings.SendHttpMessage.SendhttpextraparametersLong),
            new CommandLineArgument(OPTION_SENDLEVEL, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttplevelShort, Strings.SendHttpMessage.SendhttplevelLong(ParsedResultType.Success.ToString(), ParsedResultType.Warning.ToString(), ParsedResultType.Error.ToString(), ParsedResultType.Fatal.ToString(), "All"), DEFAULT_LEVEL, null, Enum.GetNames(typeof(ParsedResultType)).Union(new string[] { "All" } ).ToArray()),
            new CommandLineArgument(OPTION_SENDALL, CommandLineArgument.ArgumentType.Boolean, Strings.SendHttpMessage.SendhttpanyoperationShort, Strings.SendHttpMessage.SendhttpanyoperationLong),

            new CommandLineArgument(OPTION_VERB, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.HttpverbShort, Strings.SendHttpMessage.HttpverbLong, "POST"),
            new CommandLineArgument(OPTION_LOG_LEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.OptionLoglevelShort, Strings.ReportHelper.OptionLoglevelLong, DEFAULT_LOG_LEVEL.ToString(), null, Enum.GetNames(typeof(Logging.LogMessageType))),
            new CommandLineArgument(OPTION_LOG_FILTER, CommandLineArgument.ArgumentType.String, Strings.ReportHelper.OptionLogfilterShort, Strings.ReportHelper.OptionLogfilterLong),
            new CommandLineArgument(OPTION_MAX_LOG_LINES, CommandLineArgument.ArgumentType.Integer, Strings.ReportHelper.OptionmaxloglinesShort, Strings.ReportHelper.OptionmaxloglinesLong, DEFAULT_LOGLINES.ToString()),

            new CommandLineArgument(OPTION_RESULT_FORMAT, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.ResultFormatShort, Strings.ReportHelper.ResultFormatLong(Enum.GetNames(typeof(ResultExportFormat))), DEFAULT_EXPORT_FORMAT.ToString(), null, Enum.GetNames(typeof(ResultExportFormat))),

            new CommandLineArgument(OPTION_URL_FORM, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpurlsformShort, Strings.SendHttpMessage.SendhttpurlsformLong),
            new CommandLineArgument(OPTION_URL_JSON, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.SendhttpurlsjsonShort, Strings.SendHttpMessage.SendhttpurlsjsonLong),

            new CommandLineArgument(OPTION_ACCEPT_ANY_CERTIFICATE, CommandLineArgument.ArgumentType.Boolean, Strings.SendHttpMessage.AcceptAnyCertificateShort, Strings.SendHttpMessage.AcceptAnyCertificateLong),
            new CommandLineArgument(OPTION_ACCEPT_SPECIFIED_CERTIFICATE, CommandLineArgument.ArgumentType.String, Strings.SendHttpMessage.AcceptSpecifiedCertificateShort, Strings.SendHttpMessage.AcceptSpecifiedCertificateLong),

            new CommandLineArgument(OPTION_SEND_HTTP_RETRIES, CommandLineArgument.ArgumentType.Integer, Strings.SendHttpMessage.SendHttpRetriesShort, Strings.SendHttpMessage.SendHttpRetriesLong, DEFAULT_RETRIES.ToString()),
            new CommandLineArgument(OPTION_SEND_HTTP_RETRY_DELAY, CommandLineArgument.ArgumentType.Integer, Strings.SendHttpMessage.SendHttpRetryDelayShort, Strings.SendHttpMessage.SendHttpRetryDelayLong, DEFAULT_RETRY_DELAY),
        ];

        protected override string SubjectOptionName => OPTION_MESSAGE;
        protected override string BodyOptionName => OPTION_MESSAGE;
        protected override string ActionLevelOptionName => OPTION_SENDLEVEL;
        protected override string ActionOnAnyOperationOptionName => OPTION_SENDALL;
        protected override string LogLevelOptionName => OPTION_LOG_LEVEL;
        protected override string LogFilterOptionName => OPTION_LOG_FILTER;
        protected override string LogLinesOptionName => OPTION_MAX_LOG_LINES;
        protected override string ResultFormatOptionName => OPTION_RESULT_FORMAT;
        protected override string ExtraDataOptionName => OPTION_EXTRA_PARAMETERS;

        /// <summary>
        /// This method is the interception where the module can interact with the execution environment and modify the settings.
        /// </summary>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
        protected override bool ConfigureModule(IDictionary<string, string> commandlineOptions)
        {
            var reportTargets = new List<SendRequestType>();

            // Grab the legacy URL option if it exists, and add it to the appropriate list
            commandlineOptions.TryGetValue(OPTION_URL, out var legacy_urls);
            if (!string.IsNullOrEmpty(legacy_urls))
            {
                if (!commandlineOptions.TryGetValue(OPTION_RESULT_FORMAT, out var format))
                    format = ResultExportFormat.Duplicati.ToString();

                if (!Enum.TryParse<ResultExportFormat>(format, true, out var exportFormat))
                    exportFormat = ResultExportFormat.Duplicati;

                commandlineOptions.TryGetValue(OPTION_VERB, out var verb);
                if (string.IsNullOrEmpty(verb))
                    verb = "POST";

                reportTargets.AddRange(legacy_urls.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(url => new SendRequestType(url, verb, exportFormat)));
            }

            // Get the options as passed
            commandlineOptions.TryGetValue(OPTION_URL_FORM, out var formurls);
            if (!string.IsNullOrWhiteSpace(formurls))
                reportTargets.AddRange(formurls.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(url => new SendRequestType(url, "POST", ResultExportFormat.Duplicati)));

            commandlineOptions.TryGetValue(OPTION_URL_JSON, out var jsonurls);
            if (!string.IsNullOrWhiteSpace(jsonurls))
                reportTargets.AddRange(jsonurls.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(url => new SendRequestType(url, "POST", ResultExportFormat.Json)));

            //We need at least one URL to report to
            if (reportTargets.Count == 0)
                return false;

            m_report_targets = reportTargets.DistinctBy(x => (x.Url, x.Verb, x.Format)).ToList();

            commandlineOptions.TryGetValue(OPTION_MESSAGE_PARAMETER_NAME, out m_messageParameterName);
            if (string.IsNullOrEmpty(m_messageParameterName))
                m_messageParameterName = DEFAULT_MESSAGE_PARAMETER_NAME;

            commandlineOptions.TryGetValue(OPTION_EXTRA_PARAMETERS, out m_extraParameters);
            m_acceptAnyCertificate = Utility.Utility.ParseBoolOption(commandlineOptions.AsReadOnly(), OPTION_ACCEPT_ANY_CERTIFICATE);
            m_acceptSpecificCertificates = commandlineOptions.ContainsKey(OPTION_ACCEPT_SPECIFIED_CERTIFICATE) ? commandlineOptions[OPTION_ACCEPT_SPECIFIED_CERTIFICATE].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries) : null;

            m_retries = Utility.Utility.ParseIntOption(commandlineOptions.AsReadOnly(), OPTION_SEND_HTTP_RETRIES, DEFAULT_RETRIES);
            m_retryDelay = Utility.Utility.ParseTimespanOption(commandlineOptions.AsReadOnly(), OPTION_SEND_HTTP_RETRY_DELAY, DEFAULT_RETRY_DELAY);

            return true;
        }

        #endregion

        private async Task<Exception> SendMessage(HttpClient client, SendRequestType target, string subject, string body)
        {
            byte[] data;
            MediaTypeHeaderValue contenttype;

            if (target.Format == ResultExportFormat.Json)
            {
                contenttype = new MediaTypeHeaderValue("application/json");
                data = Encoding.UTF8.GetBytes(body);
            }
            else
            {
                const int chuncksize = 2048;
                contenttype = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                var sb = new StringBuilder();
                sb.Append(m_messageParameterName).Append("=");
                for (int i = 0; i < body.Length; i += chuncksize)
                    sb.Append(System.Uri.EscapeDataString(body.Substring(i, Math.Min(chuncksize, body.Length - i))));

                if (!string.IsNullOrEmpty(m_extraParameters))
                    sb.Append("&").Append(m_extraParameters);

                data = Encoding.UTF8.GetBytes(sb.ToString());
            }

            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(target.Url),
                Method = new HttpMethod(target.Verb),
                Content = new ByteArrayContent(data)
            };
            request.Content.Headers.ContentType = contenttype;

            Exception lastEx = null;

            await RetryHelper.Retry(async () =>
            {
                var response = await client.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                Logging.Log.WriteVerboseMessage(LOGTAG, "HttpResponseMessage",
                    "HTTP Response to {0}: {1} - {2}: {3}",
                    target.Url,
                    ((int)response.StatusCode).ToString(),
                    response.ReasonPhrase,
                    responseContent
                );

                response.EnsureSuccessStatusCode();
                lastEx = null;
            }, (ex, retry) =>
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "HttpResponseError", ex, "HTTP Response request attempt {0} of {1} failed for: {2}", retry, m_retries, target.Url);
                lastEx = ex;
            }, m_retries, m_retryDelay, CancellationToken.None);

            return lastEx;
        }

        private Dictionary<ResultExportFormat, string> m_cachedBodyResults;

        protected override string ReplaceTemplate(string input, object result, Exception exception, bool subjectline)
        {
            // No need to do the expansion as we throw away the result
            if (subjectline)
                return string.Empty;

            if (m_report_targets == null)
                return string.Empty;


            m_cachedBodyResults = m_report_targets
                .Select(x => x.Format)
                .Distinct()
                .ToDictionary(
                    x => x,
                    x => base.ReplaceTemplate(input, result, exception, false, x)
                );

            return string.Empty;
        }

        protected override void SendMessage(string subject, string body)
        {
            if (m_report_targets == null || m_cachedBodyResults == null)
                return;

            using HttpClientHandler httpHandler = new HttpClientHandler();
            HttpClientHelper.ConfigureHandlerCertificateValidator(httpHandler, m_acceptAnyCertificate, m_acceptSpecificCertificates);

            using var client = HttpClientHelper.CreateClient(httpHandler);
            // Explicitly keeping the default 100s timeout

            Exception ex = null;

            foreach (var target in m_report_targets)
            {
                if (m_cachedBodyResults.TryGetValue(target.Format, out var result))
                    ex ??= SendMessage(client, target, subject, result).Await();
            }

            if (ex != null)
                throw ex;
        }
    }
}
