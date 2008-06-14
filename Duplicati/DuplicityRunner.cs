#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using Duplicati.Datamodel;
using System.Drawing;

namespace Duplicati
{
    /// <summary>
    /// This class encapsulates all communication with Duplicity
    /// </summary>
    public class DuplicityRunner
    {
        private StringDictionary m_environment;

        public DuplicityRunner(string apppath, StringDictionary environment)
        {
            if (!apppath.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                apppath += System.IO.Path.DirectorySeparatorChar;
            System.Environment.SetEnvironmentVariable(ApplicationSettings.APP_PATH_ENV.Substring(1, ApplicationSettings.APP_PATH_ENV.Length - 2) , apppath);
            m_environment = new StringDictionary();
            if (environment != null)
                foreach (string k in environment.Keys)
                    m_environment[k] = environment[k];
        }

        public void Execute(Schedule schedule)
        {
            if (schedule.Tasks == null || schedule.Tasks.Count == 0)
                throw new Exception("No tasks were assigned to the schedule");

            Task task = schedule.Tasks[0];
            List<string> args = new List<string>();

            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();

            StringDictionary env = psi.EnvironmentVariables;

            foreach (string key in m_environment.Keys)
                env[key] = m_environment[key];

            lock (Program.MainLock)
            {
                env["PATH"] = System.Environment.ExpandEnvironmentVariables(Program.ApplicationSettings.PGPPath) + System.IO.Path.PathSeparator + env["PATH"];

                args.Add("\"" + System.Environment.ExpandEnvironmentVariables(Program.ApplicationSettings.DuplicityPath) + "\"");


                args.Add("incremental");
                args.Add("\"" + System.Environment.ExpandEnvironmentVariables(task.SourcePath) + "\"");
                args.Add("\"" + System.Environment.ExpandEnvironmentVariables(task.GetDestinationPath()) + "\"");

                if (!string.IsNullOrEmpty(schedule.FullAfter))
                {
                    args.Add("--full-if-older-than");
                    args.Add(schedule.FullAfter);
                }


                if (string.IsNullOrEmpty(task.Encryptionkey))
                    args.Add("--no-encryption");
                else
                    env["PASSPHRASE"] = task.Encryptionkey;

                if (!string.IsNullOrEmpty(task.Signaturekey))
                {
                    args.Add("--sign-key");
                    args.Add(task.Signaturekey);
                }

                task.GetExtraSettings(args, env);


                psi.CreateNoWindow = true;
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                psi.FileName = System.Environment.ExpandEnvironmentVariables(Program.ApplicationSettings.PythonPath);
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardInput = false;
                psi.UseShellExecute = false;
                lock (Program.MainLock)
                    psi.WorkingDirectory = System.IO.Path.GetDirectoryName(psi.FileName);
            }

            psi.Arguments = string.Join(" ", args.ToArray());

            System.Diagnostics.Process p = new System.Diagnostics.Process();
            p.StartInfo = psi;
            p.Start();

            p.WaitForExit();


            string errorstream = p.StandardError.ReadToEnd();
            string outstream = p.StandardOutput.ReadToEnd();

            string logentry = "";
            if (!string.IsNullOrEmpty(errorstream))
            {
                string tmp = errorstream.Replace("gpg: CAST5 encrypted data", "").Replace("gpg: encrypted with 1 passphrase", "").Trim();

                if (tmp.Length > 0)
                    logentry += "** Error stream: \r\n" + errorstream + "\r\n**\r\n";
            }
            logentry += outstream;

            lock (Program.MainLock)
            {
                LogBlob lb = task.DataParent.Add<LogBlob>();
                lb.StringData = logentry;

                Log l = task.DataParent.Add<Log>();
                l.LogBlob = lb;
                task.Logs.Add(l);

                //Keep some of the data in an easy to read manner
                DuplicityOutputParser.ParseData(l);
                l.SubAction = "Primary";
                l.Action = "Backup";
            }


            if (schedule.KeepFull > 0)
            {
                p = new System.Diagnostics.Process();
                p.StartInfo = psi;

                args[1] = "remove-all-but-n-full";
                args[2] = schedule.KeepFull.ToString();
                args.Add("--force");
                p.StartInfo.Arguments = string.Join(" ", args.ToArray());            

                p.Start();

                p.WaitForExit();

                errorstream = p.StandardError.ReadToEnd();
                outstream = p.StandardOutput.ReadToEnd();

                logentry = "";
                if (!string.IsNullOrEmpty(errorstream))
                    logentry += "** Error stream: \r\n" + errorstream + "\r\n**\r\n";
                logentry += outstream;

                lock (Program.MainLock)
                {
                    LogBlob lb = task.DataParent.Add<LogBlob>();
                    lb.StringData = logentry;

                    Log l = task.DataParent.Add<Log>();
                    l.LogBlob = lb;
                    task.Logs.Add(l);

                    //Keep some of the data in an easy to read manner
                    DuplicityOutputParser.ParseData(l);
                    l.SubAction = "Cleanup";
                    l.Action = "Backup";
                }
            }

            if (!string.IsNullOrEmpty(schedule.KeepTime))
            {
                p = new System.Diagnostics.Process();
                p.StartInfo = psi;

                args[1] = "remove-older-than";
                args[2] = schedule.KeepTime;
                if (!args.Contains("--force"))
                    args.Add("--force");
                p.StartInfo.Arguments = string.Join(" ", args.ToArray());

                p.Start();

                p.WaitForExit();

                errorstream = p.StandardError.ReadToEnd();
                outstream = p.StandardOutput.ReadToEnd();

                logentry = "";
                if (!string.IsNullOrEmpty(errorstream))
                    logentry += "\r\n** Error stream: \r\n" + errorstream + "\r\n**\r\n";
                logentry += outstream;

                lock (Program.MainLock)
                {
                    LogBlob lb = task.DataParent.Add<LogBlob>();
                    lb.StringData = logentry;

                    Log l = task.DataParent.Add<Log>();
                    l.LogBlob = lb;
                    task.Logs.Add(l);

                    //Keep some of the data in an easy to read manner
                    DuplicityOutputParser.ParseData(l);
                    l.SubAction = "Cleanup";
                    l.Action = "Backup";
                }
            }

            lock (Program.MainLock)
            {
                //TODO: Fix this once commit recursive is implemented
                task.DataParent.CommitAll();
                Program.DataConnection.CommitAll();
            }
        }
    }
}
