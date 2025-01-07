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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Duplicati.Library.Utility;

namespace Duplicati.Library
{
    public class OAuthHttpClient : HttpClient
    {
        private static readonly string USER_AGENT_VERSION = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        private readonly OAuthHttpMessageHandler m_authenticator;

        public OAuthHttpClient(string authid, string protocolKey)
            : this(CreateMessageHandler(authid, protocolKey))
        {
        }

        private OAuthHttpClient(OAuthHttpMessageHandler authenticator)
            : base(authenticator, true)
        {
            this.m_authenticator = authenticator;

            // Set the overall timeout
            if (HttpContextSettings.OperationTimeout > TimeSpan.Zero)
            {
                this.Timeout = HttpContextSettings.OperationTimeout;
            }
            else
            {
                // If no timeout is set, default to infinite
                this.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            }

            // Set the default user agent
            this.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Duplicati", USER_AGENT_VERSION));
        }

        /// <summary>
        /// Hide the base GetAsync method to throw a TimeoutException when an HTTP timeout occurs.
        /// </summary>
        public new async Task<System.Net.Http.HttpResponseMessage> GetAsync(string requestUri)
        {
            // The HttpClient.GetAsync method throws an OperationCanceledException when the timeout is exceeded.
            // In order to provide a more informative exception, we will detect this case and throw a TimeoutException
            // instead.
            try
            {
                return await base.GetAsync(requestUri).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Since there is no CancellationToken, we will assume that the OperationCanceledException
                // is due to an HTTP timeout.
                throw new TimeoutException($"HTTP timeout {this.Timeout} exceeded.");
            }
        }

        /// <summary>
        /// Sends an async request with optional authentication.
        /// </summary>
        /// <param name="request">Http request</param>
        /// <param name="authenticate">Whether to authenticate the request</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>Http response</returns>
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool authenticate, CancellationToken cancellationToken)
        {
            if (!authenticate)
            {
                this.PreventAuthentication(request);
            }

            // The HttpCompletionOptions are a nice way to emulate the BufferRequests behavior.
            // When set to ResponseContentRead, async call will not complete until the response has been
            // read and is cached in memory (somehow).
            // When set to ResponseHeadersRead, it looks like both Mono and .NET don't buffer the result.
            HttpCompletionOption httpCompletionOption = HttpContextSettings.BufferRequests ? HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead;

            // The HttpClient.SendAsync method throws an OperationCanceledException when the timeout is exceeded.
            // In order to provide a more informative exception, we will detect this case and throw a TimeoutException
            // instead. This will also allow the BackendUploader to differentiate between cancellations requested by
            // the user and those generated by timeouts.
            try
            {
                return await this.SendAsync(request, httpCompletionOption, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"HTTP timeout {this.Timeout} exceeded.");
            }
        }

        /// <summary>
        /// Prevents authentication from being applied on the given request
        /// </summary>
        /// <param name="request">Request to not authenticate</param>
        /// <returns>Request to not authenticate</returns>
        public HttpRequestMessage PreventAuthentication(HttpRequestMessage request)
        {
            return this.m_authenticator.PreventAuthentication(request);
        }

        /// <summary>
        /// Create a message handler with the global timeout / certificate settings.
        /// </summary>
        /// <param name="authid">OAuth Auth-ID</param>
        /// <param name="protocolKey">Protocol key</param>
        /// <returns>Http message handler</returns>
        private static OAuthHttpMessageHandler CreateMessageHandler(string authid, string protocolKey)
        {
            OAuthHttpMessageHandler handler = new OAuthHttpMessageHandler(authid, protocolKey);

            /* TODO-DNC - not supported https://github.com/dotnet/corefx/issues/26223
            // Set the read/write timeout
            if (HttpContextSettings.ReadWriteTimeout > TimeSpan.Zero)
            {
                // TODO: This is no longer supported, OAuthHelper should be rewritten
                // handler.ReadWriteTimeout = (int)HttpContextSettings.ReadWriteTimeout.TotalMilliseconds;
            }

            // Set the certificate validator
            if (HttpContextSettings.CertificateValidator != null)
            {
                // TODO: This is no longer supported, the validation can now be done pr. connection as it should always have been
                // handler.ServerCertificateValidationCallback = HttpContextSettings.CertificateValidator.ValidateServerCertficate;
            }
            */
            return handler;
        }
    }
}
