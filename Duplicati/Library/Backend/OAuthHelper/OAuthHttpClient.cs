﻿//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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

        static OAuthHttpClient()
        {
            // There is a regression in some versions of Mono (6.0) where an HttpClient
            // with a BaseAddress cannot make requests to URLs that begin with '/'.
            // https://github.com/mono/mono/issues/14630
            // https://www.mono-project.com/docs/faq/known-issues/urikind-relativeorabsolute/.
            if (Utility.Utility.IsMono)
            {
                FieldInfo field = typeof(System.Uri).GetField("useDotNetRelativeOrAbsolute", BindingFlags.Static | BindingFlags.GetField | BindingFlags.NonPublic);
                field?.SetValue(null, true);
            }
        }

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
            // instead.  This will also allow the BackendUploader to differentiate between cancellations requested by
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

            // Set the read/write timeout
            if (HttpContextSettings.ReadWriteTimeout > TimeSpan.Zero)
            {
                handler.ReadWriteTimeout = (int)HttpContextSettings.ReadWriteTimeout.TotalMilliseconds;
            }

            // Set the certificate validator
            if (HttpContextSettings.CertificateValidator != null)
            {
                handler.ServerCertificateValidationCallback = HttpContextSettings.CertificateValidator.ValidateServerCertficate;
            }

            return handler;
        }
    }
}
