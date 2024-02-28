// Copyright (C) 2024, The Duplicati Team
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
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using Duplicati.Library.Common;

namespace Duplicati.Library.Utility
{
    public class SslCertificateValidator
    {
        [Serializable]
        public class InvalidCertificateException : Exception
        {
            private readonly string m_certificate = null;
            private readonly SslPolicyErrors m_errors = SslPolicyErrors.None;

            public string Certificate { get { return m_certificate; } }
            public SslPolicyErrors SslError { get { return m_errors; } }

            public InvalidCertificateException(string certificate, SslPolicyErrors error)
                : base(Strings.SslCertificateValidator.VerifyCertificateException(error, certificate) + (Platform.IsClientPosix ? Strings.SslCertificateValidator.MonoHelpSSL : ""))
            {
                m_certificate = certificate;
                m_errors = error;
            }
        }

        public SslCertificateValidator(bool acceptAll, string[] validHashes)
        {
            m_acceptAll = acceptAll;
            m_validHashes = validHashes;
        }

        private readonly bool m_acceptAll = false;
        private readonly string[] m_validHashes = null;

        public bool ValidateServerCertficate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            if (m_acceptAll)
                return true;

            string certHash = null;

            try
            {
                certHash = Utility.ByteArrayAsHexString(cert.GetCertHash());
                if (certHash != null && m_validHashes != null)
                {
                    foreach (var hash in m_validHashes)
                    {
                        if (!string.IsNullOrEmpty(hash) && certHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(Strings.SslCertificateValidator.VerifyCertificateHashError(ex, sslPolicyErrors), ex);
            }

            return false;
        }
    }
}
