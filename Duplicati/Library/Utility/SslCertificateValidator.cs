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

#nullable enable

using System;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Duplicati.Library.Utility;

public class SslCertificateValidator(bool acceptAll, string[]? validHashes, bool ignoreRevocationFailure = false)
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

    /// <summary>
    /// The chain status flags that are treated as revocation check failures
    /// and ignored when <see cref="ignoreRevocationFailure"/> is set.
    /// </summary>
    private static readonly X509ChainStatusFlags RevocationFailureFlags =
        X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;

    public bool ValidateServerCertificate(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (acceptAll)
            return true;

        try
        {
            DateTime now = DateTime.Now;

            using var certificate = cert as X509Certificate2 ?? new X509Certificate2(cert ?? throw new ArgumentNullException(nameof(cert)));

            // Validate date range before anything else, reject expired certs
            if (!IsDateValid(certificate, now))
                return false;

            // Check if the certificate is directly approved by comparing the hash
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


            // If requested, ignore revocation check failures (e.g. OCSP server offline or status unknown)
            if (ignoreRevocationFailure)
                sslPolicyErrors = FilterRevocationErrors(sslPolicyErrors, chain);

            if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) || sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
                throw new InvalidCertificateException(certificate.GetCertHashString(), sslPolicyErrors);

            // If no hash is found, perform the standard validations
            return sslPolicyErrors == SslPolicyErrors.None && certificate.Verify();
        }
        catch (InvalidCertificateException)
        {
            // Propagate the typed certificate exception directly so that backend call-sites
            // can detect it without relying on unwrapping exceptions
            throw;
        }
        catch (Exception ex)
        {
            throw new Exception(Strings.SslCertificateValidator.VerifyCertificateHashError(ex, sslPolicyErrors), ex);
        }
    }

    /// <summary>
    /// Removes the revocation-related chain errors from the reported SSL policy errors
    /// when the caller has requested that revocation failures be ignored.
    /// </summary>
    /// <param name="sslPolicyErrors">The original SSL policy errors</param>
    /// <param name="chain">The certificate chain, if any</param>
    /// <returns>The filtered SSL policy errors</returns>
    private static SslPolicyErrors FilterRevocationErrors(SslPolicyErrors sslPolicyErrors, X509Chain? chain)
    {
        if (chain?.ChainStatus == null || chain.ChainStatus.Length == 0)
            return sslPolicyErrors;

        // Strip the revocation flags from each chain status entry. If no entry retains any
        // non-revocation status afterwards, the RemoteCertificateChainErrors flag was set
        // solely due to revocation check failures and can be cleared. Entries that carry an
        // unrelated status (e.g. UntrustedRoot, PartialChain) keep it, so genuine chain
        // errors are still reported and the connection is rejected.
        if (chain.ChainStatus.All(s => (s.Status & ~RevocationFailureFlags) == 0))
            return sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors;

        return sslPolicyErrors;
    }

    private bool IsTrustedHash(string hash) =>
        !string.IsNullOrWhiteSpace(hash) &&
        validHashes != null &&
        validHashes.Any(validHash => !string.IsNullOrEmpty(validHash) &&
                                     hash.Equals(validHash, StringComparison.OrdinalIgnoreCase));
    private bool IsDateValid(X509Certificate2 cert, DateTime now) => now <= cert.NotAfter && now >= cert.NotBefore;

}