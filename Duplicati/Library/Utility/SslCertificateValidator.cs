// Copyright (C) 2025, The Duplicati Team
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
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Duplicati.Library.Utility;

public class SslCertificateValidator(bool acceptAll, string[] validHashes)
{
    [Serializable]
    public class InvalidCertificateException : Exception
    {
        private readonly string m_certificate;
        private readonly SslPolicyErrors m_errors = SslPolicyErrors.None;

        public string Certificate => m_certificate;
        public SslPolicyErrors SslError => m_errors;

        public InvalidCertificateException(string certificate, SslPolicyErrors error)
            : base(Strings.SslCertificateValidator.VerifyCertificateException(error, certificate))
        {
            m_certificate = certificate;
            m_errors = error;
        }
    }

    public bool ValidateServerCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
            
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        if (acceptAll)
            return true;

        string certHash;

        try
        {
            certHash = Utility.ByteArrayAsHexString(cert.GetCertHash());
            if (certHash != null && validHashes != null)
            {
                foreach (var hash in validHashes)
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