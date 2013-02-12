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
    public class SharpExpectProcess
    {
        private System.Diagnostics.Process m_process;
        private object m_lock = new object();
        private System.Threading.Thread m_stdOutReader;
        private System.Threading.Thread m_stdErrReader;
        private Queue<string> m_stdOut = new Queue<string>();
        private Queue<string> m_stdErr = new Queue<string>();
        private System.Threading.AutoResetEvent m_event = new System.Threading.AutoResetEvent(false);
        
        public SharpExpectProcess(System.Diagnostics.Process process)
        {
            m_process = process;

            if (m_process.StartInfo.RedirectStandardError)
            {
                m_stdErrReader = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(StreamReader));
                m_stdErrReader.Start(m_process.StandardError);
            }

            if (m_process.StartInfo.RedirectStandardOutput)
            {
                m_stdOutReader = new System.Threading.Thread(new System.Threading.ParameterizedThreadStart(StreamReader));
                m_stdOutReader.Start(m_process.StandardOutput);
            }
        }

        public string GetNextOutputLine(int millisecondsTimeout)
        {
            //Is output buffered?
            lock (m_lock)
            {
                if (m_stdOut.Count > 0)
                    return m_stdOut.Dequeue();

                if (m_stdErr.Count > 0)
                    return m_stdErr.Dequeue();
            }

            if (millisecondsTimeout > 0)
            {
                m_event.WaitOne(millisecondsTimeout);
                return GetNextOutputLine(0);
            }
            else
                return null;
        }

        public int Expect(int millisecondsTimeout, params string[] possibilities)
        {
            List<KeyValuePair<System.Text.RegularExpressions.Regex, int>> lst = new List<KeyValuePair<System.Text.RegularExpressions.Regex, int>>();
            for (int i = 0; i < possibilities.Length; i++)
                if (!string.IsNullOrEmpty(possibilities[i]))
                    lst.Add(new KeyValuePair<System.Text.RegularExpressions.Regex, int>(new System.Text.RegularExpressions.Regex(possibilities[i]), i));

            return Expect<int>(millisecondsTimeout, lst);
        }

        public T Expect<T>(int millisecondsTimeout, List<KeyValuePair<string, T>> possibilities)
        {
            List<KeyValuePair<System.Text.RegularExpressions.Regex, T>> lst = new List<KeyValuePair<System.Text.RegularExpressions.Regex, T>>();
            for (int i = 0; i < possibilities.Count; i++)
                if (!string.IsNullOrEmpty(possibilities[i].Key))
                    lst.Add(new KeyValuePair<System.Text.RegularExpressions.Regex, T>(new System.Text.RegularExpressions.Regex(possibilities[i].Key), possibilities[i].Value));

            return Expect<T>(millisecondsTimeout, possibilities);
        }

        public T Expect<T>(int millisecondsTimeout, List<KeyValuePair<System.Text.RegularExpressions.Regex ,T>> possibilities)
        {
            if (possibilities == null || possibilities.Count == 0)
                return default(T);

            string line = GetNextOutputLine(millisecondsTimeout);
            if (line == null)
                return default(T);

            foreach (KeyValuePair<System.Text.RegularExpressions.Regex, T> expr in possibilities)
                if (expr.Key.Match(line).Success)
                    return expr.Value;

            return default(T);
        }

        public void Sendline(string line)
        {
            m_process.StandardInput.WriteLine(line);
        }

        private void StreamReader(object input)
        {
            char[] buf = new char[1024];
            System.IO.StreamReader sr = (System.IO.StreamReader)input;
            Queue<string> queue = sr == m_process.StandardError ? m_stdErr : m_stdOut;

            while (!m_process.HasExited)
            {
                int r = sr.Read(buf, 0, buf.Length);
                if (r > 0)
                {
                    lock (m_lock)
                    {
                        queue.Enqueue(new string(buf, 0, r));
                        m_event.Set();
                    }
                }
            }
        }

        public System.Diagnostics.Process Process { get { return m_process; } }


        public static SharpExpectProcess Spawn(string FileName, string Arguments)
        {
            System.Diagnostics.ProcessStartInfo pi = new System.Diagnostics.ProcessStartInfo(FileName, Arguments);
            pi.RedirectStandardError = true;
            pi.RedirectStandardOutput = true;
            pi.RedirectStandardInput = true;
            return Spawn(pi);
        }

        public static SharpExpectProcess Spawn(System.Diagnostics.ProcessStartInfo startInfo)
        {
            return new SharpExpectProcess(System.Diagnostics.Process.Start(startInfo));
        }
    }
}
