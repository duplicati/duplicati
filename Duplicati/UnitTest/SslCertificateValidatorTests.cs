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
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Duplicati.Library.Certificates;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Unit tests for <see cref="SslCertificateValidator"/>, covering the accept-all,
/// date-validity (checked first), pinned-hash (checked after date) and
/// ignore-revocation-failure code paths.
/// </summary>
[TestFixture]
[Category("Certificates")]
public class SslCertificateValidatorTests
{
    /// <summary>
    /// Generates an expired self-signed certificate (notAfter in the past) for testing
    /// the date-validity-first behaviour and its interaction with pinned hashes.
    /// </summary>
    private static X509Certificate2 GenerateExpiredCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=Duplicati Expired Test, O=Duplicati, C=US",
            key,
            HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication") },
                false));

        // Expired a day ago, started two days ago
        var notBefore = DateTimeOffset.UtcNow.AddDays(-2);
        var notAfter = DateTimeOffset.UtcNow.AddDays(-1);

        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return X509CertificateLoader.LoadCertificate(certificate.RawData);
    }

    /// <summary>
    /// Generates a not-yet-valid self-signed certificate (notBefore in the future).
    /// </summary>
    private static X509Certificate2 GenerateNotYetValidCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=Duplicati Future Test, O=Duplicati, C=US",
            key,
            HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));

        // Starts tomorrow, expires next year
        var notBefore = DateTimeOffset.UtcNow.AddDays(1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(365);

        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return X509CertificateLoader.LoadCertificate(certificate.RawData);
    }

    /// <summary>
    /// The validator propagates <see cref="SslCertificateValidator.InvalidCertificateException"/>
    /// directly (it is not wrapped by the outer catch-all) so backend call-sites and the
    /// remote-operation handler can detect it via its concrete type. This helper asserts that
    /// direct throw and returns the exception so tests can inspect the certificate hash and SSL error.
    /// </summary>
    private static SslCertificateValidator.InvalidCertificateException AssertThrowsInvalidCert(TestDelegate code)
    {
        var ex = Assert.Throws<SslCertificateValidator.InvalidCertificateException>(code);
        Assert.IsNotNull(ex, "Expected an InvalidCertificateException to be thrown.");
        return ex!;
    }

    #region acceptAll

    [Test]
    public void AcceptAll_ReturnsTrueForAnyCertificate()
    {
        var caPair = CertificateGenerator.GenerateCACertificate();
        var validator = new SslCertificateValidator(true, null, false);

        var result = validator.ValidateServerCertificate(
            null, caPair.Certificate, null, SslPolicyErrors.None);

        Assert.IsTrue(result);
    }

    [Test]
    public void AcceptAll_ReturnsTrueEvenForExpiredCertificate()
    {
        using var expired = GenerateExpiredCertificate();
        var validator = new SslCertificateValidator(true, null, false);

        var result = validator.ValidateServerCertificate(
            null, expired, null, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.IsTrue(result);
    }

    [Test]
    public void AcceptAll_ReturnsTrueEvenForNameMismatch()
    {
        var caPair = CertificateGenerator.GenerateCACertificate();
        var validator = new SslCertificateValidator(true, null, false);

        var result = validator.ValidateServerCertificate(
            null, caPair.Certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch);

        Assert.IsTrue(result);
    }

    #endregion

    #region Untrusted self-signed cert on the default path

    [Test]
    public void NoHashes_UntrustedSelfSignedCert_NoErrors_ReturnsFalse()
    {
        // A self-signed CA cert is not in the OS trust store, so certificate.Verify()
        // (the final check on the default path) returns false even with no SSL policy errors.
        var caPair = CertificateGenerator.GenerateCACertificate();
        var validator = new SslCertificateValidator(false, null, false);

        var result = validator.ValidateServerCertificate(
            null, caPair.Certificate, null, SslPolicyErrors.None);

        Assert.IsFalse(result);
    }

    [Test]
    public void IgnoreRevocationFailure_UntrustedSelfSignedCert_NoErrors_ReturnsFalse()
    {
        // The revocation filter only clears RemoteCertificateChainErrors when the chain
        // status is revocation-only; it never bypasses the OS trust check. With no SSL
        // policy errors the filter is a no-op and the default path still calls
        // certificate.Verify(), which rejects an untrusted self-signed CA.
        var caPair = CertificateGenerator.GenerateCACertificate();
        var validator = new SslCertificateValidator(false, null, true);

        var result = validator.ValidateServerCertificate(
            null, caPair.Certificate, null, SslPolicyErrors.None);

        Assert.IsFalse(result);
    }

    #endregion

    #region Pinned hash (checked after date validity)

    [Test]
    public void PinnedHash_MatchingHash_ReturnsTrueForValidCertificate()
    {
        var caPair = CertificateGenerator.GenerateCACertificate();
        var hash = caPair.Certificate.GetCertHashString();
        var validator = new SslCertificateValidator(false, new[] { hash }, false);

        var result = validator.ValidateServerCertificate(
            null, caPair.Certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch);

        Assert.IsTrue(result);
    }

    [Test]
    public void PinnedHash_MatchingHash_ExpiredCertificate_ReturnsFalse()
    {
        // Date validity is checked before the pinned hash, so an expired certificate is
        // rejected even when its hash is pinned. Pinning affects trust, not duration.
        using var expired = GenerateExpiredCertificate();
        var hash = expired.GetCertHashString();
        var validator = new SslCertificateValidator(false, new[] { hash }, false);

        var result = validator.ValidateServerCertificate(
            null, expired, null, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.IsFalse(result);
    }

    [Test]
    public void PinnedHash_NonMatchingHash_ExpiredCertificate_ReturnsFalse()
    {
        // An expired cert with a pinned hash that does NOT match is rejected by the
        // date-validity check, which runs before the pinned-hash check.
        using var expired = GenerateExpiredCertificate();
        var validator = new SslCertificateValidator(false, new[] { "00FF00FF00FF" }, false);

        var result = validator.ValidateServerCertificate(
            null, expired, null, SslPolicyErrors.None);

        Assert.IsFalse(result);
    }

    [Test]
    public void PinnedHash_CaseInsensitiveMatch()
    {
        var caPair = CertificateGenerator.GenerateCACertificate();
        // GetCertHashString returns uppercase hex; pass lowercase to verify case-insensitivity
        var hash = caPair.Certificate.GetCertHashString().ToLowerInvariant();
        var validator = new SslCertificateValidator(false, new[] { hash }, false);

        var result = validator.ValidateServerCertificate(
            null, caPair.Certificate, null, SslPolicyErrors.None);

        Assert.IsTrue(result);
    }

    [Test]
    public void PinnedHash_EmptyHashEntriesAreIgnored()
    {
        var caPair = CertificateGenerator.GenerateCACertificate();
        var realHash = caPair.Certificate.GetCertHashString();
        // Empty/whitespace entries must not match anything
        var validator = new SslCertificateValidator(false, new[] { "", "   ", realHash }, false);

        var result = validator.ValidateServerCertificate(
            null, caPair.Certificate, null, SslPolicyErrors.None);

        Assert.IsTrue(result);
    }

    #endregion

    #region Date validity (default/un-pinned path)

    [Test]
    public void NoHashes_ExpiredCertificate_ReturnsFalse()
    {
        using var expired = GenerateExpiredCertificate();
        var validator = new SslCertificateValidator(false, null, false);

        var result = validator.ValidateServerCertificate(
            null, expired, null, SslPolicyErrors.None);

        Assert.IsFalse(result);
    }

    [Test]
    public void NoHashes_NotYetValidCertificate_ReturnsFalse()
    {
        using var future = GenerateNotYetValidCertificate();
        var validator = new SslCertificateValidator(false, null, false);

        var result = validator.ValidateServerCertificate(
            null, future, null, SslPolicyErrors.None);

        Assert.IsFalse(result);
    }

    #endregion

    #region Chain errors throw wrapped InvalidCertificateException

    [Test]
    public void NameMismatch_WithNoPinnedHash_ThrowsInvalidCertificateException()
    {
        // Capture the hash before the call: ValidateServerCertificate wraps the cert in a
        // `using` and disposes it on the throwing path, invalidating the handle afterwards.
        var caPair = CertificateGenerator.GenerateCACertificate();
        var expectedHash = caPair.Certificate.GetCertHashString();
        var validator = new SslCertificateValidator(false, null, false);

        var inner = AssertThrowsInvalidCert(() =>
            validator.ValidateServerCertificate(
                null, caPair.Certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch));

        Assert.AreEqual(expectedHash, inner.Certificate);
        Assert.AreEqual(SslPolicyErrors.RemoteCertificateNameMismatch, inner.SslError);
    }

    [Test]
    public void ChainErrors_WithNoPinnedHash_ThrowsInvalidCertificateException()
    {
        // Capture the hash before the call: ValidateServerCertificate wraps the cert in a
        // `using` and disposes it on the throwing path, invalidating the handle afterwards.
        var caPair = CertificateGenerator.GenerateCACertificate();
        var expectedHash = caPair.Certificate.GetCertHashString();
        var validator = new SslCertificateValidator(false, null, false);

        var inner = AssertThrowsInvalidCert(() =>
            validator.ValidateServerCertificate(
                null, caPair.Certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));

        Assert.AreEqual(expectedHash, inner.Certificate);
        Assert.AreEqual(SslPolicyErrors.RemoteCertificateChainErrors, inner.SslError);
    }

    [Test]
    public void InvalidCertificateException_PreservesErrorAndCertificate()
    {
        var certHash = "DEADBEEF";
        var error = SslPolicyErrors.RemoteCertificateNameMismatch;
        var ex = new SslCertificateValidator.InvalidCertificateException(certHash, error);

        Assert.AreEqual(certHash, ex.Certificate);
        Assert.AreEqual(error, ex.SslError);
        Assert.IsTrue(ex.Message.Contains(certHash));
    }

    #endregion

    #region Null certificate

    [Test]
    public void NullCertificate_Throws()
    {
        var validator = new SslCertificateValidator(false, null, false);

        // The validator throws ArgumentNullException inside the try block, which the
        // outer catch-all wraps in a generic Exception.
        var ex = Assert.Throws<Exception>(() =>
            validator.ValidateServerCertificate(null, null, null, SslPolicyErrors.None));
        Assert.IsNotNull(ex);
        Assert.IsInstanceOf<ArgumentNullException>(ex!.InnerException);
    }

    #endregion

    #region Ignore revocation failure

    [Test]
    public void IgnoreRevocationFailure_NameMismatchStillThrows()
    {
        // The revocation filter must not clear a name-mismatch error.
        var caPair = CertificateGenerator.GenerateCACertificate();
        var validator = new SslCertificateValidator(false, null, true);

        AssertThrowsInvalidCert(() =>
            validator.ValidateServerCertificate(
                null, caPair.Certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Test]
    public void IgnoreRevocationFailure_NullChain_KeepsChainErrorsAndThrows()
    {
        // With a null chain the filter is a no-op (guard at the start of
        // FilterRevocationErrors), so a chain error must still be rejected.
        var caPair = CertificateGenerator.GenerateCACertificate();
        var validator = new SslCertificateValidator(false, null, true);

        AssertThrowsInvalidCert(() =>
            validator.ValidateServerCertificate(
                null, caPair.Certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Test]
    public void IgnoreRevocationFailure_NonRevocationChainStatus_DoesNotClearChainErrors()
    {
        // Build a real chain for an untrusted self-signed cert. The platform produces a
        // non-revocation status (e.g. UntrustedRoot). The filter's strict .All(...) predicate
        // must NOT clear RemoteCertificateChainErrors in that case, so the validator rejects.
        var caPair = CertificateGenerator.GenerateCACertificate();

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.Build(caPair.Certificate);

        // Sanity: the platform must produce at least one non-revocation status for this
        // assertion to be meaningful; otherwise the environment does not exercise the
        // strict-branch of the filter and we cannot assert deterministically.
        var revocationFlags =
            X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;
        var hasNonRevocationStatus =
            chain.ChainStatus.Any(s => (s.Status & ~revocationFlags) != 0);
        if (!hasNonRevocationStatus)
        {
            Assert.Ignore(
                "Platform did not produce a non-revocation chain status " +
                $"(got: {string.Join(", ", chain.ChainStatus.Select(s => s.Status.ToString()))}). " +
                "Cannot deterministically verify the strict branch of the revocation filter.");
        }

        var validator = new SslCertificateValidator(false, null, true);

        // A chain error must still be rejected because the chain status is not revocation-only.
        AssertThrowsInvalidCert(() =>
            validator.ValidateServerCertificate(
                null, caPair.Certificate, chain, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Test]
    public void IgnoreRevocationFailure_EmptyChainStatus_KeepsChainErrorsAndThrows()
    {
        // When the chain has no status entries at all, FilterRevocationErrors returns early
        // (guard at the start) and leaves RemoteCertificateChainErrors untouched, so the
        // validator rejects. We build a chain that verifies cleanly to get an empty ChainStatus.
        var caPair = CertificateGenerator.GenerateCACertificate();

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
        chain.ChainPolicy.ExtraStore.Add(caPair.Certificate);
        chain.Build(caPair.Certificate);

        // If the platform still surfaces status entries despite the relaxed flags, the test
        // would not exercise the empty-chain branch; skip in that case.
        if (chain.ChainStatus.Length > 0)
        {
            Assert.Ignore(
                $"Platform produced chain status entries despite relaxed flags " +
                $"(got: {string.Join(", ", chain.ChainStatus.Select(s => s.Status.ToString()))}). " +
                "Cannot deterministically verify the empty-chain branch.");
        }

        var validator = new SslCertificateValidator(false, null, true);

        AssertThrowsInvalidCert(() =>
            validator.ValidateServerCertificate(
                null, caPair.Certificate, chain, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Test]
    public void IgnoreRevocationFailure_NotEnabled_KeepsChainErrorsAndThrows()
    {
        // When the option is OFF, a chain error must be rejected even if the chain
        // status is revocation-only (the filter only runs when the flag is set).
        var caPair = CertificateGenerator.GenerateCACertificate();
        var validator = new SslCertificateValidator(false, null, false);

        AssertThrowsInvalidCert(() =>
            validator.ValidateServerCertificate(
                null, caPair.Certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    #endregion

    #region Default parameter / construction

    [Test]
    public void Constructor_OmitsIgnoreRevocationFailure_DefaultsToFalse()
    {
        // The optional parameter defaults to false, so the revocation filter must
        // NOT run and a chain error must be rejected.
        var caPair = CertificateGenerator.GenerateCACertificate();
        var validator = new SslCertificateValidator(false, null);

        AssertThrowsInvalidCert(() =>
            validator.ValidateServerCertificate(
                null, caPair.Certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    #endregion
}
