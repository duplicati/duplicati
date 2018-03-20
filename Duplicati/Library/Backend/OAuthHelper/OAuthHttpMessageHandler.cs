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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library
{
    public class OAuthHttpMessageHandler : HttpClientHandler
    {
        /// <summary>
        /// Requests which contain a property with this name (in 'request.Properties') will not have the authentication header automatically added.
        /// </summary>
        public const string DISABLE_AUTHENTICATION_PROPERTY = "OAuthHttpMessageHandler_DisableAuthentication";

        private OAuthHelper m_oauth;

        public OAuthHttpMessageHandler(string authid, string protocolKey)
        {
            this.m_oauth = new OAuthHelper(authid, protocolKey);
        }

        /// <summary>
        /// Prevents authentication from being applied on the given request
        /// </summary>
        /// <param name="request">Request to not authenticate</param>
        /// <returns>Request to not authenticate</returns>
        public HttpRequestMessage PreventAuthentication(HttpRequestMessage request)
        {
            request.Properties[DISABLE_AUTHENTICATION_PROPERTY] = true;
            return request;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Properties.ContainsKey(DISABLE_AUTHENTICATION_PROPERTY))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.m_oauth.AccessToken);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
