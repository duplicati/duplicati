using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace Duplicati.Library.Backend
{
    internal class SslCertificateValidator : IDisposable
    {
        public class InvalidCertificateException : Exception
        {
            private string m_certificate = null;
            private SslPolicyErrors m_errors = SslPolicyErrors.None;

            public string Certificate { get { return m_certificate; } }
            public SslPolicyErrors SslError { get { return m_errors; } }

            public InvalidCertificateException(string certificate, SslPolicyErrors error)
                : base(string.Format(Strings.SslCertificateValidator.VerifyCertificateException, error, certificate))
            {
                m_certificate = certificate;
                m_errors = error;
            }
        }

        public SslCertificateValidator(bool acceptAll, string validHash)
        {
            Activate(acceptAll, validHash);
        }

        private static bool m_acceptAll = false;
        private static string m_validHash = null;
        private static bool m_isAttached = false;
        private static Exception m_uncastException = null;
        private static RemoteCertificateValidationCallback m_oldCallback = null;
        internal static string m_lastCertificate = null;

        private static void Activate(bool acceptAll, string validHash)
        {
            if (m_isAttached)
                throw new InvalidOperationException(Strings.SslCertificateValidator.InvalidCallSequence);

            m_acceptAll = acceptAll;
            m_validHash = validHash;
            m_oldCallback = System.Net.ServicePointManager.ServerCertificateValidationCallback;

            System.Net.ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(ValidateServerCertficate);
            m_isAttached = true;
        }

        private static void Deactivate()
        {
            if (!m_isAttached)
                throw new InvalidOperationException(Strings.SslCertificateValidator.InvalidCallSequence);
            System.Net.ServicePointManager.ServerCertificateValidationCallback = m_oldCallback;
            m_oldCallback = null;
            m_isAttached = false;

            if (m_uncastException != null)
            {
                Exception tmp = m_uncastException;
                m_uncastException = null;
                throw tmp;
            }
        }
        
        private static bool ValidateServerCertficate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            if (m_acceptAll)
                return true;

            m_lastCertificate = null;

            try
            {
                string certHash = Core.Utility.ByteArrayAsHexString(cert.GetCertHash());
                m_lastCertificate = certHash;
                if (certHash != null && certHash.Equals(m_validHash, StringComparison.InvariantCultureIgnoreCase))
                    return true;

                Console.WriteLine(string.Format(Strings.SslCertificateValidator.VerifyCertificateException, sslPolicyErrors, certHash));
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format(Strings.SslCertificateValidator.VerifyCertificateHashError, ex, sslPolicyErrors), ex);
            }

            m_uncastException = new InvalidCertificateException(m_lastCertificate, sslPolicyErrors);
            return false;
        }


        #region IDisposable Members

        public void Dispose()
        {
            if (m_isAttached)
                Deactivate();
        }

        #endregion
    }
}
