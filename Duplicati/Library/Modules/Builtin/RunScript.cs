//  Copyright (C) 2015, The Duplicati Team

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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Duplicati.Library.Utility;
using Duplicati.Library.Interface;
using System.Linq;
using Duplicati.Library.Modules.Builtin.ResultSerialization;

namespace Duplicati.Library.Modules.Builtin
{
    public class RunScript : Duplicati.Library.Interface.IGenericCallbackModule
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<RunScript>();
        /// <summary>
        /// The default log level
        /// </summary>
        private const Logging.LogMessageType DEFAULT_LOG_LEVEL = Logging.LogMessageType.Warning;

        private const string STARTUP_OPTION = "run-script-before";
        private const string FINISH_OPTION = "run-script-after";
        private const string REQUIRED_OPTION = "run-script-before-required";
        private const string TIMEOUT_OPTION = "run-script-timeout";
        /// <summary>
        /// Option used to set the log level for mail reports
        /// </summary>
        private const string OPTION_LOG_LEVEL = "run-script-log-level";
        /// <summary>
        /// Option used to set the log filters for mail reports
        /// </summary>
        private const string OPTION_LOG_FILTER = "run-script-log-filter";

        private const string RESULT_FORMAT_OPTION = "run-script-result-output-format";

        private const string DEFAULT_TIMEOUT = "60s";

        private string m_requiredScript = null;
        private string m_startScript = null;
        private string m_finishScript = null;
        private int m_timeout = 0;

        private string m_operationName;
        private string m_remoteurl;
        private string[] m_localpath;
        private IDictionary<string, string> m_options;
        private IResultFormatSerializer resultFormatSerializer;

        /// <summary>
        /// The log scope that should be disposed
        /// </summary>
        private IDisposable m_logscope;
        /// <summary>
        /// The log storage
        /// </summary>
        private Utility.FileBackedStringList m_logstorage;


        #region IGenericModule implementation
        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            commandlineOptions.TryGetValue(STARTUP_OPTION, out m_startScript);
            commandlineOptions.TryGetValue(REQUIRED_OPTION, out m_requiredScript);
            commandlineOptions.TryGetValue(FINISH_OPTION, out m_finishScript);

            string tmpResultFormat;
            ResultExportFormat resultFormat;
            if (!commandlineOptions.TryGetValue(RESULT_FORMAT_OPTION, out tmpResultFormat)) {
                resultFormat = ResultExportFormat.Duplicati;
            }
            else if (!Enum.TryParse(tmpResultFormat, true, out resultFormat)) {
                resultFormat = ResultExportFormat.Duplicati;
            }

            resultFormatSerializer = ResultFormatSerializerProvider.GetSerializer(resultFormat);

            string t;
            if (!commandlineOptions.TryGetValue(TIMEOUT_OPTION, out t))
                t = DEFAULT_TIMEOUT;

            m_timeout = (int)Utility.Timeparser.ParseTimeSpan(t).TotalMilliseconds;

            m_options = commandlineOptions;

            m_options.TryGetValue(OPTION_LOG_FILTER, out var logfilterstring);
            var filter = Utility.FilterExpression.ParseLogFilter(logfilterstring);
            var logLevel = Utility.Utility.ParseEnumOption(m_options, OPTION_LOG_LEVEL, DEFAULT_LOG_LEVEL);

            m_logstorage = new FileBackedStringList();
            m_logscope = Logging.Log.StartScope(m => m_logstorage.Add(m.AsString(true)), m => {

                if (filter.Matches(m.FilterTag, out var result, out var match))
                    return result;
                else if (m.Level < logLevel)
                    return false;

                return true;
            });
        }

        public string Key { get { return "runscript"; } }
        public string DisplayName { get { return Strings.RunScript.DisplayName; } }
        public string Description { get { return Strings.RunScript.Description; } }
        public bool LoadAsDefault  { get { return true; } }

        public IList<Duplicati.Library.Interface.ICommandLineArgument> SupportedCommands
        {
            get
            {
                string[] resultOutputFormatOptions = new string[] { ResultExportFormat.Duplicati.ToString(), ResultExportFormat.Json.ToString() };
                return new List<Duplicati.Library.Interface.ICommandLineArgument>(new Duplicati.Library.Interface.ICommandLineArgument[] {
                    new Duplicati.Library.Interface.CommandLineArgument(STARTUP_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.RunScript.StartupoptionShort, Strings.RunScript.StartupoptionLong),
                    new Duplicati.Library.Interface.CommandLineArgument(FINISH_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.RunScript.FinishoptionShort, Strings.RunScript.FinishoptionLong),
                    new Duplicati.Library.Interface.CommandLineArgument(REQUIRED_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, Strings.RunScript.RequiredoptionShort, Strings.RunScript.RequiredoptionLong),
                    new CommandLineArgument(RESULT_FORMAT_OPTION,
                        CommandLineArgument.ArgumentType.Enumeration,
                        Strings.RunScript.ResultFormatShort,
                        Strings.RunScript.ResultFormatLong(resultOutputFormatOptions),
                        ResultExportFormat.Duplicati.ToString(),
                        null,
                        resultOutputFormatOptions),
                    new Duplicati.Library.Interface.CommandLineArgument(TIMEOUT_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan, Strings.RunScript.TimeoutoptionShort, Strings.RunScript.TimeoutoptionLong, DEFAULT_TIMEOUT),

                    new CommandLineArgument(OPTION_LOG_LEVEL, CommandLineArgument.ArgumentType.Enumeration, Strings.ReportHelper.OptionLoglevellShort, Strings.ReportHelper.OptionLoglevelLong, DEFAULT_LOG_LEVEL.ToString(), null, Enum.GetNames(typeof(Logging.LogMessageType))),
                    new CommandLineArgument(OPTION_LOG_FILTER, CommandLineArgument.ArgumentType.String, Strings.ReportHelper.OptionLogfilterShort, Strings.ReportHelper.OptionLogfilterLong),
                });
            }
        }
        #endregion

        #region IGenericCallbackModule implementation

        public void OnStart(string operationname, ref string remoteurl, ref string[] localpath)
        {
            m_operationName = operationname;
            m_remoteurl = remoteurl;
            m_localpath = localpath;


            if (!string.IsNullOrEmpty(m_requiredScript))
                Execute(m_requiredScript, "BEFORE", m_operationName, ref m_remoteurl, ref m_localpath, m_timeout, true, m_options, null, null);

            if (!string.IsNullOrEmpty(m_startScript))
                Execute(m_startScript, "BEFORE", m_operationName, ref m_remoteurl, ref m_localpath, m_timeout, false, m_options, null, null);
        }

        public void OnFinish (object result)
        {
            // Dispose the current log scope
            if (m_logscope != null)
            {
                try { m_logscope.Dispose(); }
                catch { }
                m_logscope = null;
            }

            if (string.IsNullOrEmpty(m_finishScript))
                return;

            ParsedResultType level;
            if (result is Exception)
                level = ParsedResultType.Fatal;
            else if (result != null && result is Library.Interface.IBasicResults)
                level = ((IBasicResults)result).ParsedResult;
            else
                level = ParsedResultType.Error;

            using (TempFile tmpfile = new TempFile())
            {
                using (var streamWriter = new StreamWriter(tmpfile))
                    streamWriter.Write(resultFormatSerializer.Serialize(result, m_logstorage));

                Execute(m_finishScript, "AFTER", m_operationName, ref m_remoteurl, ref m_localpath, m_timeout, false, m_options, tmpfile, level);
            }
        }
        #endregion

        private static void Execute(string scriptpath, string eventname, string operationname, ref string remoteurl, ref string[] localpath, int timeout, bool requiredScript, IDictionary<string, string> options, string datafile, ParsedResultType? level)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(scriptpath);
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                foreach(KeyValuePair<string, string> kv in options)
                    psi.EnvironmentVariables["DUPLICATI__" + kv.Key.Replace('-', '_')] = kv.Value;

                if (!options.ContainsKey("backup-name"))
                    psi.EnvironmentVariables["DUPLICATI__backup_name"] = System.IO.Path.GetFileNameWithoutExtension(Duplicati.Library.Utility.Utility.getEntryAssembly().Location);

                psi.EnvironmentVariables["DUPLICATI__EVENTNAME"] = eventname;
                psi.EnvironmentVariables["DUPLICATI__OPERATIONNAME"] = operationname;
                psi.EnvironmentVariables["DUPLICATI__REMOTEURL"] = remoteurl;
                if (level != null)
                    psi.EnvironmentVariables["DUPLICATI__PARSED_RESULT"] = level.Value.ToString();
                
                if (localpath != null)
                    psi.EnvironmentVariables["DUPLICATI__LOCALPATH"] = string.Join(System.IO.Path.PathSeparator.ToString(), localpath);

                string stderr = null;
                string stdout = null;

                if (!string.IsNullOrEmpty(datafile))
                    psi.EnvironmentVariables["DUPLICATI__RESULTFILE"] = datafile;

                using(System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    ConsoleDataHandler cs = new ConsoleDataHandler(p);

                    if (timeout <= 0)
                        p.WaitForExit();
                    else
                        p.WaitForExit(timeout);

                    if (requiredScript)
                    {
                        if (!p.HasExited)
                            throw new Duplicati.Library.Interface.UserInformationException(Strings.RunScript.ScriptTimeoutError(scriptpath), "RunScriptTimeout");
                        else if (p.ExitCode != 0)
                            throw new Duplicati.Library.Interface.UserInformationException(Strings.RunScript.InvalidExitCodeError(scriptpath, p.ExitCode), "RunScriptInvalidExitCode");
                    }

                    if (p.HasExited)
                    {
                        stderr = cs.StandardError;
                        stdout = cs.StandardOutput;
                        if (p.ExitCode != 0)
                            Logging.Log.WriteWarningMessage(LOGTAG, "InvalidExitCode", null, Strings.RunScript.InvalidExitCodeError(scriptpath, p.ExitCode));
                    }
                    else
                    {
                            Logging.Log.WriteWarningMessage(LOGTAG, "ScriptTimeout", null, Strings.RunScript.ScriptTimeoutError(scriptpath));
                    }
                }

                if (!string.IsNullOrEmpty(stderr))
                    Logging.Log.WriteWarningMessage(LOGTAG, "StdErrorNotEmpty", null, Strings.RunScript.StdErrorReport(scriptpath, stderr));

                //We only allow setting parameters on startup
                if (eventname == "BEFORE" && stdout != null)
                {
                    foreach(string rawline in stdout.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string line = rawline.Trim();
                        if (!line.StartsWith("--", StringComparison.Ordinal))
                            continue; //Ingore anything that does not start with --

                        line = line.Substring(2);
                        int lix = line.IndexOf('=');
                        if (lix == 0) //Skip --= as that makes no sense
                            continue;

                        string key;
                        string value;

                        if (lix < 0)
                        {
                            key = line.Trim();
                            value = "";
                        }
                        else
                        {
                            key = line.Substring(0, lix).Trim();
                            value = line.Substring(lix + 1).Trim();

                            if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
                                value = value.Substring(1, value.Length - 2);
                        }

                        if (string.Equals(key, "remoteurl", StringComparison.OrdinalIgnoreCase))
                        {
                            remoteurl = value;
                        }
                        else if (string.Equals(key, "localpath", StringComparison.OrdinalIgnoreCase))
                        {
                            localpath = value.Split(System.IO.Path.PathSeparator);
                        }
                        else if (
                            string.Equals(key, "eventname", StringComparison.OrdinalIgnoreCase) || 
                            string.Equals(key, "operationname", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(key, "main-action", StringComparison.OrdinalIgnoreCase) ||
                            key == ""
                        )
                        {
                            //Ignore
                        }
                        else
                            options[key] = value;

                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "ScriptExecuteError", ex, Strings.RunScript.ScriptExecuteError(scriptpath, ex.Message));
                if (requiredScript)
                    throw;
            }
        }

        private class ConsoleDataHandler
        {
            public ConsoleDataHandler(System.Diagnostics.Process p)
            {
                p.OutputDataReceived += new System.Diagnostics.DataReceivedEventHandler(HandleOutputDataReceived);
                p.ErrorDataReceived += new System.Diagnostics.DataReceivedEventHandler(HandleErrorDataReceived);

                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
            }

            private readonly StringBuilder m_standardOutput = new StringBuilder();
            private readonly StringBuilder m_standardError = new StringBuilder();
            private readonly object m_lock = new object();

            private void HandleOutputDataReceived (object sender, System.Diagnostics.DataReceivedEventArgs e)
            {
                lock(m_lock)
                    m_standardOutput.AppendLine(e.Data);
            }

            private void HandleErrorDataReceived (object sender, System.Diagnostics.DataReceivedEventArgs e)
            {
                lock(m_lock)
                    m_standardError.AppendLine(e.Data);
            }

            public string StandardOutput
            {
                get
                {
                    lock(m_lock)
                        return m_standardOutput.ToString().Trim();
                }
            }

            public string StandardError
            {
                get
                {
                    lock(m_lock)
                        return m_standardError.ToString().Trim();
                }
            }
        }
    }
}

