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
using System.Text;

namespace Duplicati.Library.SharpExpect
{
    /// <summary>
    /// This class is a primitive version of the Expect program for Linux.
    /// It eases the manipulation of a remote process by waiting for certain
    /// patterns in the program output.
    /// It does not yet support raw reading and writing of the tty/pty devices in Linux.
    /// The timimings are not entirely excact, and does not account for time spent in code, but only the waited time.
    /// </summary>
    public class SharpExpectProcess : IDisposable
    {
        public enum OutputSource
        {
            StdOut = 1,
            StedErr = 2,
            Both = 3
        }

        private System.Diagnostics.Process m_process;
        private object m_lock = new object();
        private List<string> m_stdOut = new List<string>();
        private List<string> m_stdErr = new List<string>();
        private System.Threading.AutoResetEvent m_event = new System.Threading.AutoResetEvent(false);
        private int m_defaultTimeout = 30 * 1000;
        private int m_logLimit = 100;
        private volatile bool m_stderr_done = true;
        private volatile bool m_stdout_done = true;
        private List<string> m_log = new List<string>();

        /// <summary>
        /// A value indicating if the log mechanism is enabled
        /// </summary>
        public bool LogEnabled
        {
            get { return m_log != null; }
            set
            {
                if (value != this.LogEnabled)
                {
                    if (value)
                        m_log = new List<string>();
                    else
                        m_log = null;
                }
            }
        }

        /// <summary>
        /// The number of messages to store in the process log
        /// </summary>
        public int LogLimit
        {
            get { return m_logLimit; }
            set { m_logLimit = Math.Max(0, value); }
        }

        /// <summary>
        /// The timeout used if no timeout is specified in the function call
        /// </summary>
        public int DefaultTimeout
        {
            get { return m_defaultTimeout; }
            set { m_defaultTimeout = value; }
        }

        /// <summary>
        /// Records a message in the log
        /// </summary>
        /// <param name="prefix">The prefix to store with the message</param>
        /// <param name="message">The message to record</param>
        /// <returns>The recorded message (without the prefix)</returns>
        private string RecordInLog(string prefix, string message)
        {
            if (m_log == null || message == null)
                return message;

            m_log.Add("*" + prefix + "*: " + message);
            while (m_log.Count > m_logLimit)
                m_log.RemoveAt(0);

            return message;
        }
        
        /// <summary>
        /// Constructs a new process helper around a running process
        /// </summary>
        /// <param name="process">The proccess to wrap</param>
        public SharpExpectProcess(System.Diagnostics.Process process)
        {
            m_process = process;

            if (m_process.StartInfo.RedirectStandardError)
            {
                m_stderr_done = false;
                System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(StreamReader), m_process.StandardError);
            }

            if (m_process.StartInfo.RedirectStandardOutput)
            {
                m_stdout_done = false;
                System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(StreamReader), m_process.StandardOutput);
            }
        }

        /// <summary>
        /// Returns the next output line (from stdout or stderr) but does not remove it
        /// </summary>
        /// <param name="maxWaitTime">The maximum time to wait for a new line</param>
        /// <returns>The next output line (from stdout or stderr) but does not remove it</returns>
        public string PeekNextOutputLine(int maxWaitTime)
        {
            return PeekNextOutputLine(OutputSource.Both, maxWaitTime);
        }

        /// <summary>
        /// Returns the next output line from the selected source but does not remove it
        /// </summary>
        /// <param name="source">A value indicating what streams to examine for data</param>
        /// <param name="maxWaitTime">The maximum number of milliseconds to wait for a line to be received</param>
        /// <returns>The next output line from the selected source but does not remove it, returns null if no data is available</returns>
        public string PeekNextOutputLine(OutputSource source, int maxWaitTime)
        {
            //Is there buffered output?
            lock (m_lock)
            {
                if ((source == OutputSource.Both || source == OutputSource.StdOut) && m_stdOut.Count > 0)
                    return FindNextLine(m_stdOut, maxWaitTime);

                if ((source == OutputSource.Both || source == OutputSource.StedErr) && m_stdErr.Count > 0)
                    return FindNextLine(m_stdErr, maxWaitTime);
            }

            return null;
        }

        /// <summary>
        /// Returns the next output line from either stdout or stderr, returns null if the operation times out.
        /// </summary>
        /// <returns>The next output line from either stdout or stderr, or null if the operation times out.</returns>
        public string GetNextOutputLine()
        {
            return GetNextOutputLine(m_defaultTimeout);
        }

        /// <summary>
        /// Returns the next output line from the selected sources, returns null if the operation times out.
        /// </summary>
        /// <param name="source">A value indicating what streams to examine for data</param>
        /// <returns>The next output line from the selected sources, or null if the operation times out.</returns>
        public string GetNextOutputLine(OutputSource source)
        {
            return GetNextOutputLine(source, m_defaultTimeout);
        }

        /// <summary>
        /// Returns the next output line from either stdout or stderr, returns null if the operation times out.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the line to become ready</param>
        /// <returns>The next output line from either stdout or stderr, or null if the operation times out.</returns>
        public string GetNextOutputLine(int millisecondsTimeout)
        {
            return GetNextOutputLine(OutputSource.Both, millisecondsTimeout);
        }

        /// <summary>
        /// Returns the next output line from the selected sources, returns null if the operation times out.
        /// </summary>
        /// <param name="source">A value indicating what streams to examine for data</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the line to become ready</param>
        /// <returns>The next output line from the selected sources, or null if the operation times out.</returns>
        public string GetNextOutputLine(OutputSource source, int millisecondsTimeout)
        {
            DateTime begin = DateTime.Now;

            //Is there buffered output?
            string line = GetBufferedLine(source, millisecondsTimeout);
            if (line != null)
                return line;

            //Adjust the remaining time
            if (millisecondsTimeout != System.Threading.Timeout.Infinite)
                millisecondsTimeout = Math.Max(0, millisecondsTimeout - (int)((DateTime.Now - begin).TotalMilliseconds));

            //If we are disposed, or done, there is no point in waiting
            if (m_process == null || m_process.HasExited)
            {
                //Make sure the readers are done
                while (m_process != null && !m_stderr_done)
                    System.Threading.Thread.Sleep(100);

                while (m_process != null && !m_stdout_done)
                    System.Threading.Thread.Sleep(100);

                //Make sure queue is empty
                return GetBufferedLine(source, System.Threading.Timeout.Infinite);
            }

            begin = DateTime.Now;
            //If we can wait, do so, and try again, otherwise return null
            if (millisecondsTimeout == System.Threading.Timeout.Infinite || millisecondsTimeout > 0)
                m_event.WaitOne(millisecondsTimeout, false);

            //Adjust the remaining time
            if (millisecondsTimeout != System.Threading.Timeout.Infinite)
                millisecondsTimeout = Math.Max(0, millisecondsTimeout - (int)((DateTime.Now - begin).TotalMilliseconds));

            return GetBufferedLine(source, millisecondsTimeout);
        }

        /// <summary>
        /// Helper method to extract a buffered line
        /// </summary>
        /// <param name="source">A value indicating what streams to examine</param>
        /// <param name="maxWaitTime">The time to wait for a new line</param>
        /// <returns>The next buffered line or null</returns>
        private string GetBufferedLine(OutputSource source, int maxWaitTime)
        {
            bool returnStdOut;
            bool returnStdErr;

            lock (m_lock)
            {
                //Check stdout
                returnStdOut = ((source == OutputSource.Both || source == OutputSource.StdOut) && m_stdOut.Count > 0);

                //Check stderr
                returnStdErr = ((source == OutputSource.Both || source == OutputSource.StedErr) && m_stdErr.Count > 0);

                //No data, so make sure the event is not set
                if (!returnStdOut && !returnStdErr)
                {
                    m_event.Reset();
                    return null;
                }
            }

            if (returnStdOut)
                return RecordInLog("O", ExtractNextLine(m_stdOut, maxWaitTime));
            if (returnStdErr)
                return RecordInLog("E", ExtractNextLine(m_stdErr, maxWaitTime));

            //Notify that there was no waiting data, we should not get here
            return null;
        }

        /// <summary>
        /// Internal helper to remove the next line from the queue
        /// </summary>
        /// <param name="queue">The queue to extract from</param>
        /// <param name="maxWaitTime">The maximum time to wait for a new line</param>
        /// <returns>The extracted line</returns>
        private string ExtractNextLine(List<string> queue, int maxWaitTime)
        {
            string line = FindNextLine(queue, maxWaitTime);
            if (line == null)
                return null;

            lock (m_lock)
                if (queue[0] == line)
                    queue.RemoveAt(0);
                else
                    queue[0] = queue[0].Substring(line.Length);

            return line;
        }

        /// <summary>
        /// This function attempts to reconstruct output into lines.
        /// The reader threads put raw input into their respective queues,
        /// but timing issues may mean that a line may be split over multiple
        /// queue entries, and a single entry may contain more than one line.
        /// </summary>
        /// <param name="queue">The queue to extract from</param>
        /// <param name="maxWaitTime">The time to wait for a new line</param>
        /// <returns>The next line, or null if there is no data</returns>
        private string FindNextLine(List<string> queue, int maxWaitTime)
        {
            return FindNextLine(queue, maxWaitTime, true);
        }

        /// <summary>
        /// This function attempts to reconstruct output into lines.
        /// The reader threads put raw input into their respective queues,
        /// but timing issues may mean that a line may be split over multiple
        /// queue entries, and a single entry may contain more than one line.
        /// </summary>
        /// <param name="queue">The queue to extract from</param>
        /// <param name="allowWait">A value indicating if a wait should be performed if the extracted line does not end with a linefeed</param>
        /// <param name="maxWaitTime">The time to wait for a new line</param>
        /// <returns>The extracted line, or null if there is no data</returns>
        private string FindNextLine(List<string> queue, int maxWaitTime, bool allowWait)
        {
            char leadChar = '\r';
            char trailChar = '\n';
            string tmp;

            //Make sure no-one else touches the queue
            lock (m_lock)
            {
                if (queue.Count == 0)
                    return null;

                //If the line is split over multiple entries, try to combine it back into a single line
                while (queue[0].LastIndexOfAny(new char[] { leadChar, trailChar }) < 0 && queue.Count > 1)
                {
                    queue[0] = queue[0] + queue[1];
                    queue.RemoveAt(1);
                }

                tmp = queue[0];
            }

            //If the line ends with return, then wait for a linefeed.
            //The \r\n is windows style, and the remote output may be from a windows machine, 
            //regardless of the local OS.
            if (tmp.EndsWith(leadChar.ToString()))
            {
                if(queue.Count == 1)
                    System.Threading.Thread.Sleep(500);

                lock(m_lock)
                    if (queue.Count > 1 && queue[1].Length > 0 && queue[1][0] == trailChar)
                    {
                        tmp += trailChar;
                        if (queue[1].Length > 1)
                            queue[1] = queue[1].Substring(1);
                        else
                            queue.RemoveAt(1);
                    }
            }

            //Make sure no-one else touches the queue
            lock (m_lock)
            {
                //If there is more than a single newline in the first entry, split it

                //Find out if there are newlines in the entry
                int lineBreakPos = queue[0].IndexOf(leadChar);
                if (lineBreakPos >= 0)
                {
                    lineBreakPos++;
                    if (queue[0].Length > lineBreakPos && queue[0][lineBreakPos] == trailChar)
                        lineBreakPos++;
                }
                else
                {
                    lineBreakPos = queue[0].IndexOf(trailChar);
                    if (lineBreakPos >= 0)
                        lineBreakPos++;
                }


                //If yes, extract the first line, and insert the rest of the string in the next slot
                if (lineBreakPos >= 0 && lineBreakPos != queue[0].Length)
                {
                    tmp = queue[0].Substring(0, lineBreakPos);
                    queue[0] = queue[0].Substring(tmp.Length);
                    queue.Insert(0, tmp); //This entry may also have newlines
                }
            }

            //If there is no more data, and the extracted line does not end in a linefeed, wait a little
            if (allowWait && maxWaitTime != 0 && tmp.IndexOfAny(new char[] { leadChar, trailChar }) < 0)
            {
                DateTime begin = DateTime.Now;
                m_event.WaitOne(maxWaitTime, false);
                if (maxWaitTime != System.Threading.Timeout.Infinite)
                {
                    maxWaitTime -= (int)((DateTime.Now - begin).TotalMilliseconds);
                    maxWaitTime = Math.Max(0, maxWaitTime);
                }

                lock (m_lock)
                    if (queue.Count == 1) //No new data
                        return tmp;

                return FindNextLine(queue, maxWaitTime, maxWaitTime != 0); //Extract again, but don't wait
            }
            else
                return tmp; //The line is good, just return
        }

        /// <summary>
        /// Waits until the output matches any of the patterns given
        /// </summary>
        /// <param name="possibilities">Any number of patterns to look for, the strings must be regular expressions</param>
        /// <returns>The index of the matched pattern, or -1 if none was matched</returns>
        public int Expect(params string[] possibilities)
        {
            return Expect(m_defaultTimeout, possibilities);
        }

        /// <summary>
        /// Waits until the output matches any of the patterns given
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the pattern to be matched</param>
        /// <param name="possibilities">Any number of patterns to look for, the strings must be regular expressions</param>
        /// <returns>The index of the matched pattern, or -1 if none was matched</returns>
        public int Expect(int millisecondsTimeout, params string[] possibilities)
        {
            List<KeyValuePair<System.Text.RegularExpressions.Regex, int>> lst = new List<KeyValuePair<System.Text.RegularExpressions.Regex, int>>();
            for (int i = 0; i < possibilities.Length; i++)
                if (!string.IsNullOrEmpty(possibilities[i]))
                    lst.Add(new KeyValuePair<System.Text.RegularExpressions.Regex, int>(new System.Text.RegularExpressions.Regex(possibilities[i]), i));

            KeyValuePair<int, string> match = Expect<int>(millisecondsTimeout, lst);
            if (match.Value == null)
                return -1;
            else
                return match.Key;
        }

        /// <summary>
        /// Waits until the output matches any of the patterns given, and returns the string as well as the value for the pattern.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the pattern to be matched</param>
        /// <param name="possibilities">Any number of patterns to look for, the strings must be regular expressions</param>
        /// <typeparam name="T">The type of the value object to return</typeparam>
        /// <returns>A pair with the value given, and the string matched</returns>
        public KeyValuePair<T, string> Expect<T>(int millisecondsTimeout, List<KeyValuePair<string, T>> possibilities)
        {
            List<KeyValuePair<System.Text.RegularExpressions.Regex, T>> lst = new List<KeyValuePair<System.Text.RegularExpressions.Regex, T>>();
            for (int i = 0; i < possibilities.Count; i++)
                if (!string.IsNullOrEmpty(possibilities[i].Key))
                    lst.Add(new KeyValuePair<System.Text.RegularExpressions.Regex, T>(new System.Text.RegularExpressions.Regex(possibilities[i].Key), possibilities[i].Value));

            return Expect<T>(millisecondsTimeout, possibilities);
        }

        /// <summary>
        /// Waits until the output matches any of the patterns given, and returns the string as well as the value for the pattern.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the pattern to be matched</param>
        /// <param name="possibilities">Any number of patterns to look for</param>
        /// <typeparam name="T">The type of the value object to return</typeparam>
        /// <returns>A pair with the value given, and the string matched</returns>
        public KeyValuePair<T, string> Expect<T>(int millisecondsTimeout, List<KeyValuePair<System.Text.RegularExpressions.Regex, T>> possibilities)
        {
            if (possibilities == null || possibilities.Count == 0)
                return new KeyValuePair<T,string>(default(T), null);

            DateTime expiration = millisecondsTimeout < 0 ? DateTime.Now.AddYears(5) : DateTime.Now.AddMilliseconds(millisecondsTimeout);

            string combinedLines = "";

            //string line = null;
            while (DateTime.Now <= expiration) //|| line != null)
            {
                string line = GetNextOutputLine(1000);
                if (line == null && DateTime.Now > expiration)
                    return new KeyValuePair<T, string>(default(T), null);

                if (line != null)
                {
                    combinedLines += line;

                    foreach (KeyValuePair<System.Text.RegularExpressions.Regex, T> expr in possibilities)
                        if (expr.Key.Match(combinedLines).Success)
                            return new KeyValuePair<T, string>(expr.Value, combinedLines);
                }
            }

            return new KeyValuePair<T, string>(default(T), null);
        }

        /// <summary>
        /// Sends a line to the process
        /// </summary>
        /// <param name="line">The line to send</param>
        public void Sendline(string line)
        {
            RecordInLog("I", line);
            if (m_process.HasExited)
                throw new Exception(string.Format(Backend.Strings.SharpExpectProcess.WriteAfterExitError, LogKillAndDispose()));
            m_process.StandardInput.WriteLine(line);
        }

        /// <summary>
        /// Send a password to the remote process.
        /// This is the same as the Sendline call, but does not log the value being sent.
        /// </summary>
        /// <param name="password">The password to send</param>
        public void Sendpassword(string password)
        {
            RecordInLog("I", Backend.Strings.SharpExpectProcess.PasswordMarker);
            if (m_process.HasExited)
                throw new Exception(string.Format(Backend.Strings.SharpExpectProcess.WriteAfterExitError, LogKillAndDispose()));
            m_process.StandardInput.WriteLine(password);
        }

        /// <summary>
        /// The thread method which reads data from the given StreamReader
        /// </summary>
        /// <param name="input">A StreamReader object</param>
        private void StreamReader(object input)
        {
            //Setup the threads local data and buffer
            System.IO.StreamReader sr = null;

            try
            {
                sr = (System.IO.StreamReader)input;
                char[] buf = new char[1024];
                List<string> queue = sr == m_process.StandardError ? m_stdErr : m_stdOut;

                //Keep reading while the stream is not at and
                while (!sr.EndOfStream)
                {
                    int r = sr.Read(buf, 0, buf.Length);
                    if (r > 0)
                    {
                        lock (m_lock)
                        {
                            //Notify about the waiting data
                            queue.Add(new string(buf, 0, r));
                            m_event.Set();
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    //Flag completion
                    if (sr == m_process.StandardError)
                        m_stderr_done = true;
                    else
                        m_stdout_done = true;
                }
                catch
                {
                    //If this instance is disposed, m_process is null
                }
            }
        }

        /// <summary>
        /// Returns the wrapped process, do not use this to read or write data
        /// </summary>
        public System.Diagnostics.Process Process { get { return m_process; } }

        /// <summary>
        /// Creates a process helper object from the given command
        /// </summary>
        /// <param name="FileName">The file to run</param>
        /// <param name="Arguments">Any arguments to pass to the command</param>
        /// <returns>A process helper</returns>
        public static SharpExpectProcess Spawn(string FileName, string Arguments)
        {
            System.Diagnostics.ProcessStartInfo pi = new System.Diagnostics.ProcessStartInfo(FileName, Arguments);
            pi.RedirectStandardError = true;
            pi.RedirectStandardOutput = true;
            pi.RedirectStandardInput = true;
            return Spawn(pi);
        }

        /// <summary>
        /// Creates a process helper object from the given command
        /// </summary>
        /// <param name="startInfo">A startinfo object describing the process to start</param>
        /// <returns>A process helper</returns>
        public static SharpExpectProcess Spawn(System.Diagnostics.ProcessStartInfo startInfo)
        {
            return new SharpExpectProcess(System.Diagnostics.Process.Start(startInfo));
        }

        /// <summary>
        /// Destroys the helper object and returns logged input and output, usefull for returning a debug message
        /// </summary>
        /// <returns>A string with logmessages</returns>
        public string LogKillAndDispose()
        {
            if (this.LogEnabled)
            {
                this.Dispose();

                StringBuilder sb = new StringBuilder();
                foreach (string s in m_log)
                    sb.AppendLine(s);

                while (PeekNextOutputLine(1000) != null)
                    sb.AppendLine("*U*: " + GetNextOutputLine(0));

                return sb.ToString();
            }
            else
                return Backend.Strings.SharpExpectProcess.LogDisabled;
        }

        #region IDisposable Members

        /// <summary>
        /// Kills the remote process
        /// </summary>
        public void Dispose()
        {
            if (m_process != null)
            {
                try { if (!m_process.HasExited) m_process.Kill(); }
                catch { }

                try { m_process.Dispose(); }
                catch { }

                m_process = null;
            }
        }

        #endregion
    }
}
