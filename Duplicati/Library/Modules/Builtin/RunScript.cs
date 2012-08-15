//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
using System.Text;
using System.Collections.Generic;

namespace Duplicati.Library.Modules.Builtin
{
    public class RunScript : Duplicati.Library.Interface.IGenericCallbackModule
    {
        private const string STARTUP_OPTION = "run-script-on-start";
        private const string FINISH_OPTION = "run-script-on-finish";
        private const string REQUIRED_OPTION = "run-script-on-start-required";
        private const string TIMEOUT_OPTION = "run-script-timeout";

        private const string DEFAULT_TIMEOUT = "60s";

        private string m_requiredScript = null;
        private string m_startScript = null;
        private string m_finishScript = null;
        private int m_timeout = 0;

        private string m_operationName;
        private string m_remoteurl;
        private string[] m_localpath;
        private IDictionary<string, string> m_options;

        #region IGenericModule implementation
        public void Configure(IDictionary<string, string> commandlineOptions)
        {
            commandlineOptions.TryGetValue(STARTUP_OPTION, out m_startScript);
            commandlineOptions.TryGetValue(REQUIRED_OPTION, out m_requiredScript);
            commandlineOptions.TryGetValue(FINISH_OPTION, out m_finishScript);

            string t;
            if (!commandlineOptions.TryGetValue(TIMEOUT_OPTION, out t))
                t = DEFAULT_TIMEOUT;

            m_timeout = (int)Utility.Timeparser.ParseTimeSpan(t).TotalMilliseconds;
            m_options = commandlineOptions;
        }

        public string Key { get { return "runscript"; } }
        public string DisplayName { get { return "Run script"; } }
        public string Description { get { return "Executes a script before starting an operation, and again on completion"; } }
        public bool LoadAsDefault  { get { return true; } }

        public IList<Duplicati.Library.Interface.ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<Duplicati.Library.Interface.ICommandLineArgument>(new Duplicati.Library.Interface.ICommandLineArgument[] {
                    new Duplicati.Library.Interface.CommandLineArgument(STARTUP_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, "", ""),
                    new Duplicati.Library.Interface.CommandLineArgument(FINISH_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path, "", ""),
                    new Duplicati.Library.Interface.CommandLineArgument(REQUIRED_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean, "", "", "false"),
                    new Duplicati.Library.Interface.CommandLineArgument(TIMEOUT_OPTION, Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan, "", "", DEFAULT_TIMEOUT),
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
                Execute(m_requiredScript, "START", m_operationName, ref m_remoteurl, ref m_localpath, m_timeout, true, m_options, null);

            if (!string.IsNullOrEmpty(m_startScript))
                Execute(m_startScript, "START", m_operationName, ref m_remoteurl, ref m_localpath, m_timeout, false, m_options, null);
        }

        public void OnFinish (object result)
        {
            string r;
            if (result == null)
                r = "null?";
            else if (result is System.Collections.IEnumerable) {
                StringBuilder sb = new StringBuilder();
                System.Collections.IEnumerable ie = (System.Collections.IEnumerable)result;
                System.Collections.IEnumerator ien = ie.GetEnumerator();
                ien.Reset();

                while (ien.MoveNext()) {
                    object c = ien.Current;
                    if (c == null)
                        continue;

                    if (c.GetType().IsGenericType && !c.GetType().IsGenericTypeDefinition && c.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) {
                        object key = c.GetType().GetProperty("Key").GetValue(c, null);
                        object value = c.GetType().GetProperty("Value").GetValue(c, null);
                        sb.AppendFormat("{0}: {1}{2}", key, value, Environment.NewLine);
                    } else
                        sb.AppendLine(c.ToString());
                }

                r = sb.ToString();
            } else if (result.GetType().IsArray) {
                StringBuilder sb = new StringBuilder();
                Array a = (Array)result;

                for (int i = a.GetLowerBound(0); i <= a.GetUpperBound(0); i++) {
                    object c = a.GetValue(i);

                    if (c == null)
                        continue;

                    if (c.GetType().IsGenericType && !c.GetType().IsGenericTypeDefinition && c.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) {
                        object key = c.GetType().GetProperty("Key").GetValue(c, null);
                        object value = c.GetType().GetProperty("Value").GetValue(c, null);
                        sb.AppendFormat("{0}: {1}{2}", key, value, Environment.NewLine);
                    } else
                        sb.AppendLine(c.ToString());
                }

                r = sb.ToString();
            }
            else if (result is Exception) 
            {
                Exception e = (Exception)result;
                r = string.Format("Failed: {0}{1}Details: {2}", e.Message, Environment.NewLine, e.ToString());
            }
            else 
            {
                r = result.ToString(); 
            }

            if (!string.IsNullOrEmpty(m_finishScript))
                Execute(m_finishScript, "FINISH", m_operationName, ref m_remoteurl, ref m_localpath, m_timeout, false, m_options, r);
        }
        #endregion

        private static void Execute(string scriptpath, string eventname, string operationname, ref string remoteurl, ref string[] localpath, int timeout, bool requiredScript, IDictionary<string, string> options, string stdInData)
        {
            bool catchError = true;
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                int space_ix = scriptpath.IndexOf(' ');
                if (space_ix > 0)
                {
                    psi.FileName = scriptpath.Substring(0, space_ix);
                    psi.Arguments = scriptpath.Substring(space_ix + 1);
                }
                else
                    psi.FileName = scriptpath;

                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;

                foreach(KeyValuePair<string, string> kv in options)
                    psi.EnvironmentVariables["DUPLICATI__" + kv.Key.Replace('-', '_')] = kv.Value;

                psi.EnvironmentVariables["DUPLICATI__EVENTNAME"] = eventname;
                psi.EnvironmentVariables["DUPLICATI__OPERATIONNAME"] = operationname;
                psi.EnvironmentVariables["DUPLICATI__REMOTEURL"] = remoteurl;
                if (localpath != null)
                    psi.EnvironmentVariables["DUPLICATI__LOCALPATH"] = string.Join(System.IO.Path.PathSeparator.ToString(), localpath);

                string stderr = null;
                string stdout = null;

                using(System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    ConsoleDataHandler cs = new ConsoleDataHandler(p);

                    if (!string.IsNullOrEmpty(stdInData))
                        using(System.IO.StreamWriter sw = p.StandardInput)
                            sw.Write(stdInData);

                    if (timeout <= 0)
                        p.WaitForExit();
                    else
                        p.WaitForExit(timeout);

                    if (requiredScript)
                    {
                        if (!p.HasExited)
                        {
                            catchError = false;
                            throw new Exception(string.Format("Execution of the script \"{0}\" timed out", scriptpath));
                        }
                        else if (p.ExitCode != 0)
                        {
                            catchError = false;
                            throw new Exception(string.Format("The script \"{0}\" returned with exit code {1}", scriptpath, p.ExitCode));
                        }
                    }

                    if (p.HasExited)
                    {
                        stderr = cs.StandardError;
                        stdout = cs.StandardOutput;
                        if (p.ExitCode != 0)
                            Logging.Log.WriteMessage(string.Format("The script \"{0}\" returned with exit code {1}", scriptpath, p.ExitCode), Duplicati.Library.Logging.LogMessageType.Warning);
                    }
                    else
                    {
                        Logging.Log.WriteMessage(string.Format("Execution of the script \"{0}\" timed out", scriptpath), Duplicati.Library.Logging.LogMessageType.Warning);
                    }
                }

                if (!string.IsNullOrEmpty(stderr))
                    Logging.Log.WriteMessage(string.Format("The script \"{0}\" reported error messages: {1}", scriptpath, stderr), Duplicati.Library.Logging.LogMessageType.Warning);

                //We only allow setting parameters on startup
                if (eventname == "START")
                {
                    foreach(string _line in stdout.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string line = _line.Trim();
                        if (!line.StartsWith("--"))
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

                            if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
                                value = value.Substring(1, value.Length - 2);
                        }

                        if (string.Equals(key, "remoteurl", StringComparison.InvariantCultureIgnoreCase))
                        {
                            remoteurl = value;
                        }
                        else if (string.Equals(key, "localpath", StringComparison.InvariantCultureIgnoreCase))
                        {
                            localpath = value.Split(System.IO.Path.PathSeparator);
                        }
                        else if (
                            string.Equals(key, "eventname", StringComparison.InvariantCultureIgnoreCase) || 
                            string.Equals(key, "operationname", StringComparison.InvariantCultureIgnoreCase) ||
                            string.Equals(key, "main-action", StringComparison.InvariantCultureIgnoreCase) ||
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
                Logging.Log.WriteMessage(string.Format("Error while executing script \"{0}\": {1}", scriptpath, ex.Message), Duplicati.Library.Logging.LogMessageType.Warning, ex);
                if (!catchError || parseExitCodes)
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
            private object m_lock = new object();

            private void HandleOutputDataReceived (object sender, System.Diagnostics.DataReceivedEventArgs e)
            {
                lock(m_lock)
                    m_standardOutput.Append(e.Data);
            }

            private void HandleErrorDataReceived (object sender, System.Diagnostics.DataReceivedEventArgs e)
            {
                lock(m_lock)
                    m_standardError.Append(e.Data);
            }

            public string StandardOutput
            {
                get
                {
                    lock(m_lock)
                        return m_standardOutput.ToString();
                }
            }

            public string StandardError
            {
                get
                {
                    lock(m_lock)
                        return m_standardError.ToString();
                }
            }
        }
    }
}

