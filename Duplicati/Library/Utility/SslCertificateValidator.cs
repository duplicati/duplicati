#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

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
                : base(Strings.SslCertificateValidator.VerifyCertificateException(error, certificate) + (Utility.IsClientLinux ? Strings.SslCertificateValidator.MonoHelpSSL : ""))
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
        private Exception m_uncastException = null;

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
                    foreach(var hash in m_validHashes)
                    {
                        if (!string.IsNullOrEmpty(hash) && certHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
                        return true;
                    }
            }
            catch (Exception ex)
            {
                throw new Exception(Strings.SslCertificateValidator.VerifyCertificateHashError(ex, sslPolicyErrors), ex);
            }

            m_uncastException = new InvalidCertificateException(certHash, sslPolicyErrors);
            return false;
        }
    }
}
