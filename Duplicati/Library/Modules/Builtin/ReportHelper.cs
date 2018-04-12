//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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
    public abstract class ReportHelper : Interface.IGenericCallbackModule
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<ReportHelper>();


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
        protected virtual string DEFAULT_SUBJECT { get; }= "Duplicati %OPERATIONNAME% report for %backup-name%";
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
        protected IDictionary<string, string> m_options;
        /// <summary>
        /// The parsed result level
        /// </summary>
        protected string m_parsedresultlevel = string.Empty;
        /// <summary>
        /// The maxmimum number of log lines to include
        /// </summary>
        protected int m_maxmimumLogLines;

        /// <summary>
        /// A value indicating if this instance is configured
        /// </summary>
        private bool m_isConfigured;
        /// <summary>
        /// A value indicating if this instance has been disposed
        /// </summary>
        private bool m_isDisposed;
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

            m_isConfigured = true;
            commandlineOptions.TryGetValue(SubjectOptionName, out m_subject);
            commandlineOptions.TryGetValue(BodyOptionName, out m_body);
            m_options = commandlineOptions;

            string tmp;
            commandlineOptions.TryGetValue(ActionLevelOptionName, out tmp);
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

            m_sendAll = Utility.Utility.ParseBoolOption(commandlineOptions, ActionOnAnyOperationOptionName);

            ResultExportFormat resultFormat;
            if (!commandlineOptions.TryGetValue(ResultFormatOptionName, out var tmpResultFormat))
                resultFormat = DEFAULT_EXPORT_FORMAT;
            else if (!Enum.TryParse(tmpResultFormat, true, out resultFormat))
                resultFormat = DEFAULT_EXPORT_FORMAT;

            m_resultFormatSerializer = ResultFormatSerializerProvider.GetSerializer(resultFormat);

            commandlineOptions.TryGetValue(LogLinesOptionName, out var loglinestr);
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
            m_logscope = Logging.Log.StartScope(m => m_logstorage.Add(m.AsString(true)), m => {

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
        /// <param name="subjectline">If set to <c>true</c>, the result is intended for a subject or title line.</param>
        protected virtual string ReplaceTemplate(string input, object result, bool subjectline)
        {
            input = Regex.Replace(input, "\\%OPERATIONNAME\\%", m_operationname ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            input = Regex.Replace(input, "\\%REMOTEURL\\%", m_remoteurl ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            input = Regex.Replace(input, "\\%LOCALPATH\\%", m_localpath == null ? "" : string.Join(System.IO.Path.PathSeparator.ToString(), m_localpath), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            input = Regex.Replace(input, "\\%PARSEDRESULT\\%", m_parsedresultlevel ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (subjectline)
            {
                input = Regex.Replace(input, "\\%RESULT\\%", m_parsedresultlevel ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            else
            {
                if (input.IndexOf("%RESULT%", StringComparison.OrdinalIgnoreCase) >= 0)
                    input = Regex.Replace(input, "\\%RESULT\\%", m_resultFormatSerializer.Serialize(result, LogLines), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            foreach (KeyValuePair<string, string> kv in m_options)
                input = Regex.Replace(input, "\\%" + kv.Key + "\\%", kv.Value ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!m_options.ContainsKey("backup-name"))
                input = Regex.Replace(input, "\\%backup-name\\%", System.IO.Path.GetFileNameWithoutExtension(Duplicati.Library.Utility.Utility.getEntryAssembly().Location) ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            input = Regex.Replace(input, "\\%[^\\%]+\\%", "");
            return input;
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

        public void OnFinish(object result)
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
            if (result is Exception)
                level = ParsedResultType.Fatal;
            else if (result != null && result is Library.Interface.IBasicResults)
                level = ((IBasicResults)result).ParsedResult;
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
                if (body != DEFAULT_BODY && System.IO.Path.IsPathRooted(body) && System.IO.File.Exists(body))
                    body = System.IO.File.ReadAllText(body);

                body = ReplaceTemplate(body, result, false);
                subject = ReplaceTemplate(subject, result, true);

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
