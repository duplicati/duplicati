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

using System.Collections.Generic;
using System.Net.Http;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that a connection without any certificate option is left to the
/// default validation of the OS, and does not get the custom validator
/// </summary>
[TestFixture]
[Category("Certificates")]
public class SslDefaultValidationTests
{
    /// <summary>
    /// Parses the options as a backend does, and configures a handler with them
    /// </summary>
    private static HttpClientHandler CreateHandler(Dictionary<string, string?> options)
    {
        var parsed = SslOptionsHelper.Parse(options);
        var handler = new HttpClientHandler();
        HttpClientHelper.ConfigureHandlerCertificateValidator(handler, parsed.AcceptAllCertificates, parsed.AcceptSpecificCertificateHashes, parsed.IgnoreRevocationFailure);
        return handler;
    }

    [Test]
    public void NoCertificateOptionsUseTheDefaultValidation()
    {
        using var handler = CreateHandler(new Dictionary<string, string?> { ["use-ssl"] = "true" });
        Assert.That(handler.ServerCertificateCustomValidationCallback, Is.Null);
    }

    [Test]
    public void AnEmptyHashListUsesTheDefaultValidation()
    {
        using var handler = CreateHandler(new Dictionary<string, string?> { [SslOptionsHelper.AcceptSpecifiedSslHashOption] = "" });
        Assert.That(handler.ServerCertificateCustomValidationCallback, Is.Null);
    }

    [TestCase(SslOptionsHelper.AcceptSpecifiedSslHashOption, "00FF00FF")]
    [TestCase(SslOptionsHelper.AcceptAnySslCertificateOption, "true")]
    [TestCase(SslOptionsHelper.IgnoreRevocationFailureOption, "true")]
    public void ACertificateOptionUsesTheCustomValidation(string option, string value)
    {
        using var handler = CreateHandler(new Dictionary<string, string?> { [option] = value });
        Assert.That(handler.ServerCertificateCustomValidationCallback, Is.Not.Null);
    }
}
