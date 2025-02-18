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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Duplicati.Library.Interface;
using Duplicati.Library.Modules.Builtin.ResultSerialization;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Modules.Builtin
{
    /// <summary>
    /// A helper module that contains all shared code used in the various reporting modules
    /// </summary>
    public abstract class ReportHelper : IGenericCallbackModule
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<ReportHelper>();

        /// <summary>
        /// The salt used for calculating a backup Id from the remote URL
        /// </summary>
        private const string SALT = "DUPL";

        /// <summary>
        /// Name of the option used to specify subject
        /// </summary>
        protected abstract string SubjectOptionName { get; }
        /// <summary>
        /// Name of the option used to specify the body
        /// </summary>
        protected abstract string BodyOptionName { get; }

        /// <summary>
        /// Name of the option used to specify the level on which the operation is activated
        /// </summary>
        protected abstract string ActionLevelOptionName { get; }

        /// <summary>
        /// Name of the option used to specify if reports are sent for other operations than backups
        /// </summary>
        protected abstract string ActionOnAnyOperationOptionName { get; }

        /// <summary>
        /// Name of the option used to specify the log level
        /// </summary>
        protected abstract string LogLevelOptionName { get; }

        /// <summary>
        /// Name of the option used to specify the log filter
        /// </summary>
        protected abstract string LogFilterOptionName { get; }

        /// <summary>
        /// Name of the option used to specify the maximum number of log lines to include
        /// </summary>
        protected abstract string LogLinesOptionName { get; }

        /// <summary>
        /// Name of the option used to the output format
        /// </summary>
        protected abstract string ResultFormatOptionName { get; }

        /// <summary>
        /// The default subject or title line
        /// </summary>
        protected virtual string DEFAULT_SUBJECT { get; } = "Duplicati %OPERATIONNAME% report for %backup-name%";
        /// <summary>
        /// The default report level
        /// </summary>
        protected virtual string DEFAULT_LEVEL { get; } = "all";
        /// <summary>
        /// The default report body
        /// </summary>
        protected virtual string DEFAULT_BODY { get; } = "%RESULT%";

        /// <summary>
        /// The default maximum number of log lines
        /// </summary>
        protected virtual int DEFAULT_LOGLINES { get; } = 100;

        /// <summary>
        /// The default log level
        /// </summary>
        protected virtual Logging.LogMessageType DEFAULT_LOG_LEVEL { get; } = Logging.LogMessageType.Warning;

        /// <summary>
        /// The default export format
        /// </summary>
        protected virtual ResultExportFormat DEFAULT_EXPORT_FORMAT { get; } = ResultExportFormat.Duplicati;

        /// <summary>
        /// The module key
        /// </summary>
        public abstract string Key { get; }
        /// <summary>
        /// The module display name
        /// </summary>
        public abstract string DisplayName { get; }
        /// <summary>
        /// The module description
        /// </summary>
        public abstract string Description { get; }
        /// <summary>
        /// The module default load setting
        /// </summary>
        public abstract bool LoadAsDefault { get; }
        /// <summary>
        /// The list of supported commands
        /// </summary>
        public abstract IList<ICommandLineArgument> SupportedCommands { get; }

        /// <summary>
        /// Returns the format used by the serializer
        /// </summary>
        protected ResultExportFormat ExportFormat => m_resultFormatSerializer.Format;

        /// <summary>
        /// The cached name of the operation
        /// </summary>
        protected string m_operationname;
        /// <summary>
        /// The cached remote url
        /// </summary>
        protected string m_remoteurl;
        /// <summary>
        /// The cached local path
        /// </summary>
        protected string[] m_localpath;
        /// <summary>
        /// The cached set of options
        /// </summary>
        protected IReadOnlyDictionary<string, string> m_options;
        /// <summary>
        /// The parsed result level
        /// </summary>
        protected string m_parsedresultlevel = string.Empty;
        /// <summary>
        /// The maximum number of log lines to include
        /// </summary>
        protected int m_maxmimumLogLines;

        /// <summary>
        /// A value indicating if this instance is configured
        /// </summary>
        private bool m_isConfigured;
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
        private string[] m_levels;
        /// <summary>
        /// True to send all operations
        /// </summary>
        private bool m_sendAll;
        /// <summary>
        /// The log scope that should be disposed
        /// </summary>
        private IDisposable m_logscope;
        /// <summary>
        /// The log storage
        /// </summary>
        private Utility.FileBackedStringList m_logstorage;
        /// <summary>
        /// Serializer to use when serializing the message.
        /// </summary>
        private IResultFormatSerializer m_resultFormatSerializer;

        /// <summary>
        /// Configures the module
        /// </summary>
        /// <returns><c>true</c>, if module should be used, <c>false</c> otherwise.</returns>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
        protected abstract bool ConfigureModule(IDictionary<string, string> commandlineOptions);

        /// <summary>
        /// Sends the email message
        /// </summary>
        /// <param name="subject">The subject line.</param>
        /// <param name="body">The message body.</param>
        protected abstract void SendMessage(string subject, string body);


        /// <summary>
        /// This method is the interception where the module can interact with the execution environment and modify the settings.
        /// </summary>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            if (!ConfigureModule(commandlineOptions))
                return;

            m_options = commandlineOptions.AsReadOnly();
            m_isConfigured = true;
            m_options.TryGetValue(SubjectOptionName, out m_subject);
            m_options.TryGetValue(BodyOptionName, out m_body);

            string tmp;
            m_options.TryGetValue(ActionLevelOptionName, out tmp);
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

            m_sendAll = Utility.Utility.ParseBoolOption(m_options, ActionOnAnyOperationOptionName);

            ResultExportFormat resultFormat;
            if (!m_options.TryGetValue(ResultFormatOptionName, out var tmpResultFormat))
                resultFormat = DEFAULT_EXPORT_FORMAT;
            else if (!Enum.TryParse(tmpResultFormat, true, out resultFormat))
                resultFormat = DEFAULT_EXPORT_FORMAT;

            m_resultFormatSerializer = ResultFormatSerializerProvider.GetSerializer(resultFormat);

            m_options.TryGetValue(LogLinesOptionName, out var loglinestr);
            if (!int.TryParse(loglinestr, out m_maxmimumLogLines))
                m_maxmimumLogLines = DEFAULT_LOGLINES;

            if (string.IsNullOrEmpty(m_subject))
                m_subject = DEFAULT_SUBJECT;
            if (string.IsNullOrEmpty(m_body))
                m_body = DEFAULT_BODY;

            m_options.TryGetValue(LogFilterOptionName, out var logfilterstring);
            var filter = Utility.FilterExpression.ParseLogFilter(logfilterstring);
            var logLevel = Utility.Utility.ParseEnumOption(m_options, LogLevelOptionName, DEFAULT_LOG_LEVEL);

            m_logstorage = new FileBackedStringList();
            m_logscope = Logging.Log.StartScope(m => m_logstorage.Add(m.AsString(true)), m =>
            {

                if (filter.Matches(m.FilterTag, out var result, out var match))
                    return result;
                else if (m.Level < logLevel)
                    return false;

                return true;
            });
        }

        /// <summary>
        /// Called when the operation starts
        /// </summary>
        /// <param name="operationname">The full name of the operation</param>
        /// <param name="remoteurl">The remote backend url</param>
        /// <param name="localpath">The local path, if required</param>
        public virtual void OnStart(string operationname, ref string remoteurl, ref string[] localpath)
        {
            m_operationname = operationname;
            m_remoteurl = remoteurl;
            m_localpath = localpath;
        }

        /// <summary>
        /// Helper method to perform template expansion
        /// </summary>
        /// <returns>The expanded template.</returns>
        /// <param name="input">The input template.</param>
        /// <param name="result">The result object.</param>
        /// <param name="exception">An optional exception that has stopped the backup</param>
        /// <param name="subjectline">If set to <c>true</c>, the result is intended for a subject or title line.</param>
        protected virtual string ReplaceTemplate(string input, object result, Exception exception, bool subjectline)
            => ReplaceTemplate(input, result, exception, subjectline, m_resultFormatSerializer);

        /// <summary>
        /// Helper method to perform template expansion
        /// </summary>
        /// <returns>The expanded template.</returns>
        /// <param name="input">The input template.</param>
        /// <param name="result">The result object.</param>
        /// <param name="exception">An optional exception that has stopped the backup</param>
        /// <param name="subjectline">If set to <c>true</c>, the result is intended for a subject or title line.</param>
        /// <param name="format">The format to use when serializing the result</param>
        protected virtual string ReplaceTemplate(string input, object result, Exception exception, bool subjectline, ResultExportFormat format)
            => ReplaceTemplate(input, result, exception, subjectline, ResultFormatSerializerProvider.GetSerializer(format));

        /// <summary>
        /// The operation name template key
        /// </summary>
        private const string OPERATIONNAME = "OperationName";
        /// <summary>
        /// The remote url template key
        /// </summary>
        private const string REMOTEURL = "RemoteUrl";
        /// <summary>
        /// The local path template key
        /// </summary>
        private const string LOCALPATH = "LocalPath";
        /// <summary>
        /// The parsed result template key
        /// </summary>
        private const string PARSEDRESULT = "ParsedResult";
        /// <summary>
        /// The machine id template key
        /// </summary>
        private const string MACHINE_ID = "machine-id";
        /// <summary>
        /// The backup id template key
        /// </summary>
        private const string BACKUP_ID = "backup-id";
        /// <summary>
        /// The backup name template key
        /// </summary>
        private const string BACKUP_NAME = "backup-name";
        /// <summary>
        /// The machine name template key
        /// </summary>
        private const string MACHINE_NAME = "machine-name";
        /// <summary>
        /// The operating system template key
        /// </summary>
        private const string OPERATING_SYSTEM = "operating-system";
        /// <summary>
        /// The installation type template key
        /// </summary>
        private const string INSTALLATION_TYPE = "installation-type";
        /// <summary>
        /// The destination type template key
        /// </summary>
        private const string DESTINATION_TYPE = "destination-type";
        /// <summary>
        /// The next scheduled run template key
        /// </summary>
        private const string NEXT_SCHEDULED_RUN = "next-scheduled-run";

        /// <summary>
        /// The list of regular template keys
        /// </summary>
        private static readonly IReadOnlySet<string> OPERATION_TEMPLATE_KEYS = new HashSet<string>([
            OPERATIONNAME, REMOTEURL, LOCALPATH, PARSEDRESULT
        ], StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The list of extra template keys
        /// </summary>
        private static readonly IReadOnlySet<string> EXTRA_TEMPLATE_KEYS = new HashSet<string>([
            MACHINE_ID, BACKUP_ID, BACKUP_NAME, MACHINE_NAME,
            OPERATING_SYSTEM, INSTALLATION_TYPE, DESTINATION_TYPE, NEXT_SCHEDULED_RUN
        ], StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the default value for a template key
        /// </summary>
        /// <param name="name">The name of the template key</param>
        /// <returns>The default value</returns>
        private string GetDefaultValue(string name)
        {
            switch (name)
            {
                case OPERATIONNAME:
                    return m_operationname;
                case REMOTEURL:
                    return m_remoteurl;
                case LOCALPATH:
                    return m_localpath == null ? "" : string.Join(System.IO.Path.PathSeparator.ToString(), m_localpath);
                case PARSEDRESULT:
                    return m_parsedresultlevel;
                case MACHINE_ID:
                    return AutoUpdater.DataFolderManager.MachineID;
                case BACKUP_ID:
                    return Utility.Utility.ByteArrayAsHexString(Utility.Utility.RepeatedHashWithSalt(m_remoteurl, SALT));
                case BACKUP_NAME:
                    return System.IO.Path.GetFileNameWithoutExtension(Utility.Utility.getEntryAssembly().Location);
                case MACHINE_NAME:
                    return AutoUpdater.DataFolderManager.MachineName;
                case OPERATING_SYSTEM:
                    return AutoUpdater.UpdaterManager.OperatingSystemName;
                case INSTALLATION_TYPE:
                    return AutoUpdater.UpdaterManager.PackageTypeId;
                case DESTINATION_TYPE:
                    // Only return the url scheme, as the rest could contain sensitive information
                    var ix = m_remoteurl?.IndexOf("://", StringComparison.OrdinalIgnoreCase) ?? -1;
                    return Utility.Utility.GuessScheme(m_remoteurl) ?? "file";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Helper method to perform template expansion
        /// </summary>
        /// <returns>The expanded template.</returns>
        /// <param name="input">The input template.</param>
        /// <param name="result">The result object.</param>
        /// <param name="exception">An optional exception that has stopped the backup</param>
        /// <param name="subjectline">If set to <c>true</c>, the result is intended for a subject or title line.</param>
        /// <param name="resultFormatSerializer">The serializer to use when serializing the result</param>
        protected virtual string ReplaceTemplate(string input, object result, Exception exception, bool subjectline, IResultFormatSerializer resultFormatSerializer)
        {
            // For JSON, ignore the template and just use the contents
            if (resultFormatSerializer.Format == ResultExportFormat.Json && !subjectline)
            {
                var extra = new Dictionary<string, string>();

                // Add the default values, if found in the template
                foreach (var key in OPERATION_TEMPLATE_KEYS)
                    if (input.IndexOf($"%{key}%", StringComparison.OrdinalIgnoreCase) >= 0)
                        extra[key] = GetDefaultValue(key);

                // Add any options that are whitelisted or used in the template
                foreach (KeyValuePair<string, string> kv in m_options)
                    if (EXTRA_TEMPLATE_KEYS.Contains(kv.Key) || input.IndexOf($"%{kv.Key}%", StringComparison.OrdinalIgnoreCase) >= 0)
                        extra[kv.Key] = kv.Value;

                // Add any missing default values
                foreach (var key in EXTRA_TEMPLATE_KEYS)
                    if (!extra.ContainsKey(key))
                        extra[key] = GetDefaultValue(key);

                return resultFormatSerializer.Serialize(result, exception, LogLines, extra);
            }
            else
            {

                foreach (var key in OPERATION_TEMPLATE_KEYS)
                    input = Regex.Replace(input, $"%{key}%", GetDefaultValue(key) ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (subjectline)
                {
                    input = Regex.Replace(input, "\\%RESULT\\%", m_parsedresultlevel ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                else
                {
                    if (input.IndexOf("%RESULT%", StringComparison.OrdinalIgnoreCase) >= 0)
                        input = Regex.Replace(input, "\\%RESULT\\%", resultFormatSerializer.Serialize(result, exception, LogLines, null), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }

                foreach (KeyValuePair<string, string> kv in m_options)
                    input = Regex.Replace(input, "\\%" + kv.Key + "\\%", kv.Value ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                foreach (var key in EXTRA_TEMPLATE_KEYS)
                    if (!m_options.ContainsKey(key))
                        input = Regex.Replace(input, $"%{key}%", GetDefaultValue(key) ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                // Remove any remaining template keys
                input = Regex.Replace(input, "\\%[^\\%]+\\%", "");
                return input;
            }
        }

        /// <summary>
        /// Gets the filtered set of log lines
        /// </summary>
        protected IEnumerable<string> LogLines
        {
            get
            {
                var logdata = m_logstorage.AsEnumerable();
                if (m_maxmimumLogLines > 0)
                {
                    logdata = logdata.Take(m_maxmimumLogLines);
                    if (m_logstorage.Count > m_maxmimumLogLines)
                        logdata = logdata.Concat(new string[] { $"... and {m_logstorage.Count - m_maxmimumLogLines} more" });
                }

                return logdata;
            }
        }

        public void OnFinish(IBasicResults result, Exception exception)
        {
            // Dispose the current log scope
            if (m_logscope != null)
            {
                try { m_logscope.Dispose(); }
                catch { }
                m_logscope = null;
            }

            if (!m_isConfigured)
                return;

            //If we do not report this action, then skip
            if (!m_sendAll && !string.Equals(m_operationname, "Backup", StringComparison.OrdinalIgnoreCase))
                return;

            ParsedResultType level;
            if (exception != null)
                level = ParsedResultType.Fatal;
            else if (result != null)
                level = result.ParsedResult;
            else
                level = ParsedResultType.Error;

            m_parsedresultlevel = level.ToString();

            if (string.Equals(m_operationname, "Backup", StringComparison.OrdinalIgnoreCase))
            {
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
                string subject = m_subject;
                if (body != DEFAULT_BODY)
                {
                    try
                    {
                        if (System.IO.File.Exists(body) && System.IO.Path.IsPathRooted(body))
                            body = System.IO.File.ReadAllText(body);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "ReportSubmitError", ex, "Invalid path, or unable to read file given as body");
                    }
                }

                body = ReplaceTemplate(body, result, exception, false);
                subject = ReplaceTemplate(subject, result, exception, true);

                SendMessage(subject, body);
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

                Logging.Log.WriteWarningMessage(LOGTAG, "ReportSubmitError", ex, Strings.ReportHelper.SendMessageFailedError(sb.ToString()));
            }

        }

    }
}
