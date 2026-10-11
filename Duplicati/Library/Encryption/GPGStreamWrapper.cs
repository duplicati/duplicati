// Copyright (C) 2026, The Duplicati Team
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
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Duplicati.Library.Encryption
{
    internal class GPGStreamWrapper : Utility.OverrideableStream
    {
        private System.Diagnostics.Process m_p;
        private Task m_copier;
        private Task<string> m_stderr;

        /// <summary>
        /// Wraps a crypto stream, ensuring that it is correctly disposed
        /// </summary>
        /// <param name="p">The GPG process</param>
        /// <param name="copier">The task that copies the other end of the GPG process</param>
        /// <param name="stderr">The task that reads the GPG error output</param>
        /// <param name="basestream">The stream to wrap</param>
        public GPGStreamWrapper(System.Diagnostics.Process p, Task copier, Task<string> stderr, Stream basestream)
            : base(basestream)
        {
            if (p == null)
                throw new NullReferenceException("p");
            if (copier == null)
                throw new NullReferenceException("copier");
            if (stderr == null)
                throw new NullReferenceException("stderr");

            m_p = p;
            m_copier = copier;
            m_stderr = stderr;
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (m_p != null)
                    Finish();
            }
            finally
            {
                if (m_p != null)
                {
                    KillProcess();
                    m_p.Dispose();
                    m_p = null;
                    m_copier = null;
                    m_stderr = null;
                }

                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Closes the wrapped stream and waits for GPG and the copy to finish
        /// </summary>
        private void Finish()
        {
            Exception failure = null;
            try
            {
                m_basestream.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            // GPG only finishes after it has seen the end of the input, which can take a long time for a large volume,
            // so there is no time limit here. The copy ends when GPG closes its end of the pipe, or when it fails.
            try
            {
                m_copier.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            // If the copy failed, GPG may be blocked writing output that nobody reads
            if (failure != null)
                KillProcess();

            m_p.WaitForExit();

            var errmsg = m_stderr.GetAwaiter().GetResult();
            if (errmsg.Contains("decryption failed:"))
                throw new System.Security.Cryptography.CryptographicException(Strings.GPGStreamWrapper.DecryptionError(errmsg));

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        /// <summary>
        /// Stops the GPG process if it is still running
        /// </summary>
        private void KillProcess()
        {
            try
            {
                if (!m_p.HasExited)
                    m_p.Kill(true);
            }
            catch
            {
            }
        }
    }
}
