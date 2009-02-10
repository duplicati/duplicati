using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Library.Encryption
{
    internal class GPGStreamWrapper : Core.OverrideableStream
    {
        private System.Diagnostics.Process m_p;
        private System.Threading.Thread m_t;

        /// <summary>
        /// Wraps a crypto stream, ensuring that it is correctly disposed
        /// </summary>
        /// <param name="basestream">The stream to wrape</param>
        public GPGStreamWrapper(System.Diagnostics.Process p, System.Threading.Thread t, Stream basestream)
            : base(basestream)
        {
            if (p == null)
                throw new NullReferenceException("p");
            if (t == null)
                throw new NullReferenceException("t");
            
            m_p = p;
            m_t = t;
        }

        protected override void Dispose(bool disposing)
        {
            if (m_p != null)
            {
                m_basestream.Close();

                if (!m_t.Join(5000))
                    throw new Exception("Failure while invoking GnuPG, program won't flush output");

                if (!m_p.WaitForExit(5000))
                    throw new Exception("Failure while invoking GnuPG, program won't terminate");

                if (m_p.StandardError.Peek() != -1)
                {
                    string errmsg = m_p.StandardError.ReadToEnd();
                    if (errmsg.Contains("decryption failed:"))
                        throw new Exception("Decryption failed: " + errmsg);
                }

                m_p.Dispose();
                m_p = null;

                m_t = null;
            }

            base.Dispose(disposing);
        }
    }
}
