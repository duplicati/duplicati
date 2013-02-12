using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace Duplicati.Scheduler.Utility
{
    /// <summary>
    /// Some user tools
    /// </summary>
    public static class User
    {
        public enum OutputSource { stdout, stderr };
        /// <summary>
        /// The user's application directory, created if it does not exists
        /// </summary>
        /// <param name="aSub">Package name</param>
        /// <returns>Application directory</returns>
        public static string GetApplicationDirectory(string aSub)
        {
            string Result = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), aSub);
            if (!System.IO.Directory.Exists(Result)) System.IO.Directory.CreateDirectory(Result);
            return Result;
        }
        public delegate bool ExecOutputDelegate(OutputSource aSource, string aLine);
        public static string UserName { get { return System.Security.Principal.WindowsIdentity.GetCurrent().Name; } }
        public static string UserPath { get { return UserName.Replace('\\', '@'); } }
        public static string UserDomain { get { return System.Security.Principal.WindowsIdentity.GetCurrent().Name.Split('\\')[0]; } }
        public static string ParseUserName(string aUserName, out string outDomain)
        {
            outDomain = string.Empty;
            string[] Parts = aUserName.Split('\\');
            if (Parts.Length == 1) return Parts[0];
            outDomain = Parts[0];
            return Parts[1];
        }
        public class ExecuteResult
        {
            public bool Error { get; set; }
            public string[] Stdout { get; private set; }
            public string[] Stderr { get; private set; }
            public string StdoutString { get { return string.Join("\n", Stdout ?? new string[] { "<null>" });  } }
            public string StderrString { get { return string.Join("\n", Stderr ?? new string[] { "<null>" }); } }
            public int ExitCode { get; set; }
            public Exception Ex { get; set; }
            public ExecuteResult(Exception aEx) 
            { 
                Error = true; 
                Ex = aEx;
                ExitCode = -1;
            }
            public ExecuteResult(System.Diagnostics.Process aProcess)
            {
                Ex = null;
                Error = false;
                // Silly ole DOS
                Stdout = aProcess.StandardOutput.ReadToEnd().Replace("\r", string.Empty).Split('\n');
                Stderr = aProcess.StandardError.ReadToEnd().Replace("\r", string.Empty).Split('\n');
                ExitCode = aProcess.ExitCode;
            }
            public ExecuteResult(System.Diagnostics.Process aProcess, StdHandler aOut, StdHandler aErr)
            {
                Ex = null;
                Error = false;
                Stdout = aOut.Standard;
                Stderr = aErr.Standard;
                ExitCode = aProcess.ExitCode;
            }
        }
        public static ExecuteResult Execute(string aUserName, System.Security.SecureString aCheck, string aExe, string aArgs)
        {
            return Execute(aUserName, aCheck, aExe, aArgs, string.Empty);
        }
        public static ExecuteResult Execute(string aUserName, System.Security.SecureString aCheck, string aExe, string aArgs, 
            string aStdin)
        {
            return Execute(aUserName, aCheck, aExe, aArgs, string.Empty, null);
        }
        // This will keep a stdout or stserr pipe from a process drained.  As data arrives it is placed into a
        // memory stream (basically copied); trick is the output delegate is bool; if user likes the string that
        // just came in, returning true will prevent it from being copied.
        public class StdHandler
        {
            // Am I watching out or err?  Used in output delgate call.
            public OutputSource Source { get; set; }
            // The output delegate, ignored if null
            public ExecOutputDelegate Output { get; set; }
            // Ah, the Nuffer, originally a typo; but, I liked it.  This is the copy of the stream.
            private List<string> itsNuffer = new List<string>();
            // This may be used to get the copy.
            public string[] Standard { get { return itsNuffer.ToArray(); } } 
            // The Process
            private System.Diagnostics.Process itsProcess = null;
            public StdHandler(OutputSource aSource, System.Diagnostics.Process aProcess, ExecOutputDelegate aOutput)
            {
                itsProcess = aProcess;
                Source = aSource;
                Output = aOutput;
                if (Source == OutputSource.stderr) aProcess.ErrorDataReceived += new System.Diagnostics.DataReceivedEventHandler(OutputDataReceived);
                else aProcess.OutputDataReceived += new System.Diagnostics.DataReceivedEventHandler(OutputDataReceived);
            }
            public void Start()
            {
                if (Source == OutputSource.stderr) itsProcess.BeginErrorReadLine();
                else itsProcess.BeginOutputReadLine();
            }
            private void OutputDataReceived(object sender, System.Diagnostics.DataReceivedEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.Data) && (Output == null || !Output(Source, e.Data)))
                    itsNuffer.Add(e.Data);
            }
        }
        public static ExecuteResult Execute(string aUserName, System.Security.SecureString aCheck, string aExe, string aArgs,
            string aStdin, ExecOutputDelegate aOutputAndError)
        {
            return Execute(aUserName, aCheck, aExe, aArgs, System.Diagnostics.ProcessPriorityClass.Normal, aStdin, aOutputAndError, aOutputAndError);
        }

        public static ExecuteResult Execute(string aUserName, System.Security.SecureString aCheck, string aExe, string aArgs,
            System.Diagnostics.ProcessPriorityClass aPriority,
            string aStdin, ExecOutputDelegate aOutput, ExecOutputDelegate aError)
        {
            string Domain = string.Empty;
            string User = ParseUserName(aUserName, out Domain);
            bool HasStdin = !string.IsNullOrEmpty(aStdin);
            ExecuteResult Result = null;
            try
            {
                using (System.Diagnostics.Process P = new System.Diagnostics.Process())
                {
                    P.StartInfo =
                        new System.Diagnostics.ProcessStartInfo(aExe, aArgs)
                        {
                            UseShellExecute = false,
                            RedirectStandardInput = HasStdin,
                            RedirectStandardError = true,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true,
                            UserName = User,
                            Domain = Domain,
                            Password = aCheck,
                            LoadUserProfile = true,
                        };
                    // These will read while the process is running.
                    StdHandler Out = new StdHandler(OutputSource.stdout, P, aOutput);
                    StdHandler Err = new StdHandler(OutputSource.stderr, P, aError);
                    // Go
                    P.Start();
                    // Start the handlers
                    Out.Start(); Err.Start();
                    // Send any imput to the process
                    if (HasStdin)
                    {
                        P.StandardInput.WriteLine(aStdin);
                        P.StandardInput.Flush();
                    }
                    // Gotta set priority AFTER start
                    P.PriorityClass = aPriority;
                    // And wait till it is done (timeout?)
                    P.WaitForExit();
                    // And done, build a result thingy
                    Result = new ExecuteResult(P, Out, Err);
                }
            }
            catch (Exception Ex)
            {
                Debug.Write("Execute (" + aExe + "): ");
                Debug.WriteLine(Ex);
                Result = new ExecuteResult(Ex);
            }
            return Result;
        }

        // Quick and dirty, just write stdout and stderr to console.out and console.error; current user
        public static string Run(string aExe, string aArgs)
        {
            string Result = aExe + ' ' + aArgs + '\n';
            try
            {
                using (System.Diagnostics.Process P = new System.Diagnostics.Process())
                {
                    P.StartInfo =
                        new System.Diagnostics.ProcessStartInfo(aExe, aArgs)
                        {
                            UseShellExecute = false,
                            RedirectStandardError = true,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true,
                        };
                    P.Start();
                    // And wait till it is done (timeout?)
                    P.WaitForExit();
                    Result += P.StandardOutput.ReadToEnd();
                    Result += P.StandardError.ReadToEnd();
                }
            }
            catch (Exception Ex)
            {
                Debug.Write("Run (" + aExe + "): ");
                Debug.WriteLine(Ex);
                Result += Ex.Message;
            }
            return Result;
        }
        public static bool RunAs(string aExe, string aArgs)
        {
            string Redir = string.Empty;
            if (!Duplicati.Scheduler.Utility.Tools.NoException((Action)delegate() { Redir = System.IO.Path.GetTempFileName(); }))
                return false;
            bool Result = false;
            try
            {
                using (System.Diagnostics.Process P = new System.Diagnostics.Process())
                {
                    P.StartInfo =
                        new System.Diagnostics.ProcessStartInfo("cmd", "/c " + aExe + ' ' + aArgs + " >" + Redir + " 2>&1")
                        {
                            UseShellExecute = true,
                            RedirectStandardError = false,      // Can't do with shell (dunno why)
                            RedirectStandardOutput = false,
                            CreateNoWindow = true,
                            Verb = "runas",
                        };
                    P.Start();
                    // And wait till it is done (timeout?)
                    P.WaitForExit();
                    Console.Out.WriteLine(System.IO.File.ReadAllText(Redir));
                }
                Result = true;
            }
            catch (Exception Ex)
            {
                Console.Error.Write("RunAs (" + aExe + "): ");
                Console.Error.WriteLine(Ex);
            }
            finally
            {
                System.IO.File.Delete(Redir);
            }
            return Result;
        }
        public static bool IsValid(string aUserName, System.Security.SecureString aCheck)
        {
            ExecuteResult er = Execute(aUserName, aCheck, "cmd.exe", "/c echo OK");
            Debug.WriteLine("stdout:\n{" + er.StdoutString ?? "<null>" + "}\nstderr:\n" + er.StderrString ?? "<null>" + "\nExecption:\n" + er.Ex ?? string.Empty);
            return !er.Error && er.StdoutString == "OK"; // goofy ole DOS
        }
    }
}
