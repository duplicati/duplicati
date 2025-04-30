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

        private readonly OAuthHelper m_oauth;

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
