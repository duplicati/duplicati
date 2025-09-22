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
using System.Collections.Generic;
using System.Net.Http;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Utility.Options;

/// <summary>
/// Helper class for unified use of SSL certificate options
/// </summary>
public static class SslOptionsHelper
{
    /// <summary>
    /// The name of the ssl option without a prefix
    /// </summary>
    public const string UseSSLOption = "use-ssl";
    /// <summary>
    /// The name of the accept any ssl certificate option without a prefix
    /// </summary>
    public const string AcceptAnySslCertificateOption = "accept-any-ssl-certificate";
    /// <summary>
    /// The name of the accept specified ssl hash option without a prefix
    /// </summary>
    public const string AcceptSpecifiedSslHashOption = "accept-specified-ssl-hash";

    /// <summary>
    /// Gets the SSL certificate options for certificate only
    /// </summary>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The SSL certificate options</returns>
    public static CommandLineArgument[] GetCertOnlyOptions(string? prefix = null) =>
    [
        new CommandLineArgument($"{prefix}{AcceptAnySslCertificateOption}", CommandLineArgument.ArgumentType.Boolean, Strings.SslOptionsHelper.DescriptionAcceptAnyCertificateShort, Strings.SslOptionsHelper.DescriptionAcceptAnyCertificateLong, "false"),
        new CommandLineArgument($"{prefix}{AcceptSpecifiedSslHashOption}", CommandLineArgument.ArgumentType.String, Strings.SslOptionsHelper.DescriptionAcceptHashShort, Strings.SslOptionsHelper.DescriptionAcceptHashLong)
    ];

    /// <summary>
    /// Gets the SSL certificate options for SSL only
    /// </summary>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The SSL certificate options</returns>
    public static CommandLineArgument[] GetSslOnlyOption(string? prefix = null) =>
    [
        new CommandLineArgument($"{prefix}{UseSSLOption}", CommandLineArgument.ArgumentType.Boolean, Strings.SslOptionsHelper.DescriptionUseSSLShort, Strings.SslOptionsHelper.DescriptionUseSSLLong, "false")
    ];

    /// <summary>
    /// Gets the SSL certificate options
    /// </summary>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The SSL certificate options</returns>
    public static CommandLineArgument[] GetOptions(string? prefix = null) =>
    [
        .. GetSslOnlyOption(prefix),
        .. GetCertOnlyOptions(prefix)
    ];

    /// <summary>
    /// Parses the SSL certificate options from a dictionary
    /// </summary>
    /// <param name="options">The dictionary to parse</param>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The parsed SSL certificate options</returns>
    public static SslCertificateOptions Parse(IReadOnlyDictionary<string, string?> options, string? prefix = null)
    {
        var acceptSpecificCertificatesString = options.TryGetValue($"{prefix}{AcceptSpecifiedSslHashOption}", out var value) ? value : null;
        return new SslCertificateOptions(
            Utility.ParseBoolOption(options, $"{prefix}{UseSSLOption}"),
            Utility.ParseBoolOption(options, $"{prefix}{AcceptAnySslCertificateOption}"),
            string.IsNullOrWhiteSpace(acceptSpecificCertificatesString) ? Array.Empty<string>() : acceptSpecificCertificatesString.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
        );
    }

    /// <summary>
    /// Structure to hold the SSL certificate options
    /// </summary>
    /// <param name="UseSSL">Flag to indicate if SSL is used</param>
    /// <param name="AcceptAllCertificates">Flag to accept all certificates</param>
    /// <param name="AcceptSpecificCertificateHashes">Array of specific certificate hashes to accept</param>
    public sealed record SslCertificateOptions(bool UseSSL, bool AcceptAllCertificates, string[] AcceptSpecificCertificateHashes)
    {
        /// <summary>
        /// Creates a handler with the SSL certificate options
        /// </summary>
        /// <returns>The created handler</returns>
        public HttpClientHandler CreateHandler()
        {
            var handler = new HttpClientHandler();
            return ConfigureHandler(handler);
        }

        /// <summary>
        /// Configures an existing handler with the SSL certificate options
        /// </summary>
        /// <param name="handler">The handler to configure</param>
        /// <returns>The configured handler</returns>
        public HttpClientHandler ConfigureHandler(HttpClientHandler handler)
        {
            HttpClientHelper.ConfigureHandlerCertificateValidator(handler, AcceptAllCertificates, AcceptSpecificCertificateHashes);
            return handler;
        }
    }
}
