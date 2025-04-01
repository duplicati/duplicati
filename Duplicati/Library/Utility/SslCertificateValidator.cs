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

#nullable enable

using System;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Duplicati.Library.Utility;

public class SslCertificateValidator(bool acceptAll, string[]? validHashes)
{
    [Serializable]
    public class InvalidCertificateException(string certificate, SslPolicyErrors error)
        : Exception(Strings.SslCertificateValidator.VerifyCertificateException(error, certificate))
    {
        private readonly string _mCertificate = certificate;
        private readonly SslPolicyErrors _mErrors = error;

        public string Certificate => _mCertificate;
        public SslPolicyErrors SslError => _mErrors;
    }

    public bool ValidateServerCertificate(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (acceptAll)
            return true;

        try
        {
            DateTime now = DateTime.Now;

            using var certificate = cert as X509Certificate2 ?? new X509Certificate2(cert ?? throw new ArgumentNullException(nameof(cert)));

            if (!IsDateValid(certificate, now))
                return false;

            if (validHashes != null)
            {
                // Check main certificate hash
                if (IsTrustedHash(Utility.ByteArrayAsHexString(certificate.GetCertHash())))
                    return true;

                // Check chain certificate from root for the hash (this allows custom CA certificates hashes to be added)
                if (chain?.ChainElements != null)
                    if (chain.ChainElements.Any(element => IsTrustedHash(Utility.ByteArrayAsHexString(element.Certificate.GetCertHash())) && IsDateValid(element.Certificate, now)))
                        return true;
            }

            // If no hash is found, perform the standard validations
            return sslPolicyErrors == SslPolicyErrors.None && certificate.Verify();
        }
        catch (Exception ex)
        {
            throw new Exception(Strings.SslCertificateValidator.VerifyCertificateHashError(ex, sslPolicyErrors), ex);
        }
    }

    private bool IsTrustedHash(string hash) =>
        !string.IsNullOrWhiteSpace(hash) &&
        validHashes != null &&
        validHashes.Any(validHash => !string.IsNullOrEmpty(validHash) &&
                                     hash.Equals(validHash, StringComparison.OrdinalIgnoreCase));
    private bool IsDateValid(X509Certificate2 cert, DateTime now) => now <= cert.NotAfter && now >= cert.NotBefore;

}