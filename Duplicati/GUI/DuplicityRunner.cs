#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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

namespace Duplicati.GUI
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

        private System.Diagnostics.ProcessStartInfo SetupEnv(IDuplicityTask task)
        {
            task.BeginTime = DateTime.Now;
            List<string> args = new List<string>();
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();

            StringDictionary env = psi.EnvironmentVariables;

            foreach (string key in m_environment.Keys)
                env[key] = m_environment[key];

            env["PATH"] = System.Environment.ExpandEnvironmentVariables(Program.ApplicationSettings.NcFTPPath) + System.IO.Path.PathSeparator + env["PATH"];
            env["PATH"] = System.Environment.ExpandEnvironmentVariables(Program.ApplicationSettings.PuttyPath) + System.IO.Path.PathSeparator + env["PATH"];
            env["PATH"] = System.Environment.ExpandEnvironmentVariables(Program.ApplicationSettings.PGPPath) + System.IO.Path.PathSeparator + env["PATH"];

            args.Add("\"" + System.Environment.ExpandEnvironmentVariables(Program.ApplicationSettings.DuplicityPath) + "\"");

            task.GetArguments(args);

            if (string.IsNullOrEmpty(task.Task.Encryptionkey))
                args.Add("--no-encryption");
            else
                env["PASSPHRASE"] = task.Task.Encryptionkey;


            task.Task.GetExtraSettings(args, env);

            psi.CreateNoWindow = true;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            psi.FileName = System.Environment.ExpandEnvironmentVariables(Program.ApplicationSettings.PythonPath);
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardInput = false;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = System.IO.Path.GetDirectoryName(psi.FileName);


            psi.Arguments = string.Join(" ", args.ToArray());
            return psi;
        }

        public void ExecuteTask(IDuplicityTask task)
        {
            System.Diagnostics.Process p = new System.Diagnostics.Process();
            p.StartInfo = SetupEnv(task);
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

            task.RaiseTaskCompleted(logentry);

            if (task.TaskType == DuplicityTaskType.FullBackup || task.TaskType == DuplicityTaskType.IncrementalBackup)
            {
                if (task.Schedule.KeepFull > 0)
                    ExecuteTask(new RemoveAllButNFullTask(task.Schedule, (int)task.Schedule.KeepFull));
                if (!string.IsNullOrEmpty(task.Schedule.KeepTime))
                    ExecuteTask(new RemoveOlderThanTask(task.Schedule, task.Schedule.KeepTime));
            }
        }


        private void PerformBackup(Schedule schedule, bool forceFull, string fullAfter)
        {
            if (forceFull)
                ExecuteTask(new FullBackupTask(schedule));
            else
                ExecuteTask(new IncrementalBackupTask(schedule, fullAfter));
        }

        public void Restore(Schedule schedule, DateTime when, string where)
        {
            ExecuteTask(new RestoreTask(schedule, where, when));
        }

        public string[] ListBackups(Schedule schedule)
        {
            ListBackupsTask task = new ListBackupsTask(schedule);
            ExecuteTask(task);
            return task.Backups;
        }

        public void IncrementalBackup(Schedule schedule)
        {
            PerformBackup(schedule, false, null);
        }

        public void FullBackup(Schedule schedule)
        {
            PerformBackup(schedule, true, null);
        }
    }
}
