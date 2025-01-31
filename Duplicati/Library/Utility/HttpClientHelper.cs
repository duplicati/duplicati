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

using System.Net.Http;

namespace Duplicati.Library.Utility;

/// <summary>
/// This class was created as a proxy to access the HttpClientFactory
/// in places where dependency injection is not yet implemented or
/// desirable.
/// </summary>
public static class HttpClientHelper
{
    /// <summary>
    /// Reference to the HttpClientFactory instance so we can create specific clients
    /// when the singleton pattnern is not desirable.
    /// </summary>
    private static IHttpClientFactory? _factory { get; set; }

    /// <summary>
    /// Sets the factory reference.
    /// </summary>
    /// <param name="factory">IHttpClientFactory instance</param>
    public static void Configure(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a new HttpClient instance, to be used in places where the singleton
    /// pattern is not desirable.
    /// </summary>
    /// <returns></returns>
    public static HttpClient CreateClient()
    {
        return _factory?.CreateClient() ?? new HttpClient();
    }

    /// <summary>
    /// Creates a new HttpClient instance with a specific handler, wherever its needed.
    /// </summary>
    /// <param name="handler">HttpClientHandler configured with authentication parameters</param>
    /// <returns></returns>
    public static HttpClient CreateClient(HttpClientHandler handler)
    {
        return new HttpClient(handler);
    }

    /// <summary>
    /// Configures the httphandler with the certificate validator, if needed.
    ///
    /// The parameters indicate if it should accept all certificates or only the ones specified in the
    /// hashes array.
    /// </summary>
    /// <param name="handler">The HttpClientHandler to be configured</param>
    /// <param name="acceptAllCertificates">If all certificates will be considered valid</param>
    /// <param name="acceptSpecificCertificateHashes">List of hashes of certificates that will be accepted</param>
    public static void ConfigureHandlerCertificateValidator(HttpClientHandler handler, bool acceptAllCertificates, string[] acceptSpecificCertificateHashes)
    {
        var validator = new SslCertificateValidator(acceptAllCertificates, acceptSpecificCertificateHashes);
        handler.ServerCertificateCustomValidationCallback = validator.ValidateServerCertificate;
    }
}