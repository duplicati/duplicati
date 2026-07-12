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

            // Validate date range before anything else, reject expired certs.
            // NotBefore/NotAfter are returned by .NET as DateTime with Kind=Local (the UTC
            // instant expressed in local time), so DateTime.Now (also Kind=Local) is the
            // correct comparison; using UtcNow here would skew the window by the timezone offset.
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


            // If requested, ignore revocation check failures (e.g. OCSP server offline or status unknown).
            // This strips revocation-only flags from the sslPolicyErrors that the TLS stack reported using
            // its own chain object. The explicit chain built below operates on a separate X509Chain and
            // applies the same soft-fail logic independently, so both the reported-policy path and the
            // verify path honor ignoreRevocationFailure consistently.
            if (ignoreRevocationFailure)
                sslPolicyErrors = FilterRevocationErrors(sslPolicyErrors, chain);

            if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) || sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
                throw new InvalidCertificateException(certificate.GetCertHashString(), sslPolicyErrors);

            // If no hash is found, perform the standard validations
            if (sslPolicyErrors != SslPolicyErrors.None)
                return false;

            // certificate.Verify() builds its own X509Chain with a default policy that
            // hard-fails when a revocation check cannot be completed (OCSP/CRL endpoint
            // unreachable). The OS-native TLS stacks instead soft-fail in that situation:
            // the certificate is accepted when the revocation status is merely unknown
            // (not confirmed revoked). Build the chain explicitly so we can replicate that
            // behavior and honor the ignoreRevocationFailure setting.
            //
            // Note: ChainPolicy.TrustMode is left at its default (UseSystemDefault), which
            // resolves intermediate/root trust through the OS trust store exactly as
            // certificate.Verify() did, so the trust-root behaviour of the previous
            // implementation is preserved.
            using var verifyChain = new X509Chain();
            // The primary mechanism for honoring ignoreRevocationFailure: skip the online
            // revocation fetch entirely so an unreachable OCSP/CRL endpoint cannot fail the
            // chain. The post-build soft-fail below is a safety net for platforms that
            // still surface revocation status flags even under NoCheck, not the main path.
            verifyChain.ChainPolicy.RevocationMode =
                ignoreRevocationFailure ? X509RevocationMode.NoCheck : X509RevocationMode.Online;
            bool chainValid = verifyChain.Build(certificate);

            // Soft-fail on unreachable revocation checks, but only when the caller has
            // explicitly requested it via ignoreRevocationFailure. This replicates the
            // OS-native TLS behavior of accepting a certificate whose revocation status is
            // merely unknown (OCSP/CRL endpoint unreachable) rather than confirmed revoked.
            //
            // This block is a safety net for the NoCheck mode above: on some platforms
            // Build() can still report revocation-related flags despite RevocationMode being
            // NoCheck, so strip those flags here rather than relying on NoCheck alone.
            //
            // The guard requires a non-empty ChainStatus: if Build() returned false with no
            // status flags (rare platform edge case), we must not silently accept the
            // certificate, as that would widen trust beyond what any status indicates.
            // A confirmed Revoked flag, PartialChain, or any other non-revocation status
            // still fails the chain because the All() predicate would be false. The using
            // scope ensures verifyChain is disposed even if Build() throws and the outer
            // catch wraps the exception.
            if (!chainValid && ignoreRevocationFailure && verifyChain.ChainStatus.Length > 0 &&
                verifyChain.ChainStatus.All(s => (s.Status & ~RevocationFailureFlags) == 0))
                chainValid = true;

            if (!chainValid)
            {
                var chainStatus = string.Join("; ", verifyChain.ChainStatus.Select(s => $"{s.Status}={s.StatusInformation?.Trim()}"));
                var elementDetails = verifyChain.ChainElements.Count == 0
                    ? "(no chain elements)"
                    : string.Join(" | ", verifyChain.ChainElements.Select(e =>
                    {
                        var elemStatus = e.ChainElementStatus.Length == 0
                            ? "(none)"
                            : string.Join(", ", e.ChainElementStatus.Select(s => $"{s.Status}={s.StatusInformation?.Trim()}"));
                        return $"{e.Certificate.Subject} [{elemStatus}]";
                    }));

                Duplicati.Library.Logging.Log.WriteWarningMessage(
                    "SslCertificateValidator",
                    "VerifyChainFailed",
                    null,
                    $"Certificate chain validation failed for {certificate.Subject} (issuer={certificate.Issuer}, " +
                    $"thumbprint={certificate.Thumbprint}). Chain status: {chainStatus}. Per-element: {elementDetails}");
            }
            return chainValid;
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