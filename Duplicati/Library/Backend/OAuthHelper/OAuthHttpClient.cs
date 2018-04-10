//  Copyright (C) 2018, The Duplicati Team
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

            // We would also set AllowReadStreamBuffering = HttpContextSettings.BufferRequests, except HttpClient doesn't appear to expose this.
            // However, starting in .NET 4.5, it looks like HttpClient doesn't buffer by default.
            // https://www.strathweb.com/2012/09/dealing-with-large-files-in-asp-net-web-api/

            // Set the default user agent
            this.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Duplicati", USER_AGENT_VERSION));
        }

        /// <summary>
        /// Sends an async request with optional authentication.
        /// </summary>
        /// <param name="request">Http request</param>
        /// <param name="authenticate">Whether to authenticate the request</param>
        /// <returns>Http response</returns>
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool authenticate)
        {
            if (!authenticate)
            {
                this.PreventAuthentication(request);
            }

            return await this.SendAsync(request);
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
            if (HttpContextSettings.OperationTimeout > TimeSpan.Zero)
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
