using System.Threading;
//  Copyright (C) 2015, The Duplicati Team
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
using System.Text;
using Newtonsoft.Json;
using Duplicati.Library.Utility;
using System.Net;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Backblaze
{
    public class B2AuthHelper : JSONWebHelper
    {
        private readonly string m_credentials;
        private AuthResponse m_config;
        private DateTime m_configExpires;
        internal const string AUTH_URL = "https://api.backblazeb2.com/b2api/v1/b2_authorize_account";

        public B2AuthHelper(string userid, string password)
            : base()
        {
            m_credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(userid + ":" + password));
        }

        public override async Task<HttpRequestMessage> CreateRequestAsync(string url, string method, CancellationToken cancelToken)
        {
            var r = await base.CreateRequestAsync(url, method, cancelToken);
            r.Headers.Add("Authorization", await GetAuthorizationTokenAsync(cancelToken));
            return r;
        }

        public async Task<string> GetAuthorizationTokenAsync(CancellationToken cancelToken) 
            => (await GetConfigAsync(cancelToken)).AuthorizationToken;
        public async Task<string> GetAPIUrlAsync(CancellationToken cancelToken) 
            => (await GetConfigAsync(cancelToken)).APIUrl;
        public async Task<string> GetDownloadUrlAsync(CancellationToken cancelToken) 
            => (await GetConfigAsync(cancelToken)).DownloadUrl;
        public async Task<string> GetAccountIDAsync(CancellationToken cancelToken) 
            => (await GetConfigAsync(cancelToken)).AccountID;

        private string DropTrailingSlashes(string url)
        {
            while(url.EndsWith("/", StringComparison.Ordinal))
                url = url.Substring(0, url.Length - 1);
            return url;
        }

        public string APIDnsName
        {
            get
            {
                if (m_config == null || string.IsNullOrWhiteSpace(m_config.APIUrl))
                    return null;
                return new System.Uri(m_config.APIUrl).Host;
            }
        }

        public string DownloadDnsName
        {
            get
            {
                if (m_config == null || string.IsNullOrWhiteSpace(m_config.DownloadUrl))
                    return null;
                return new System.Uri(m_config.DownloadUrl).Host;
            }
        }

        private async Task<AuthResponse> GetConfigAsync(CancellationToken cancelToken)
        {
            if (m_config == null || m_configExpires < DateTime.UtcNow)
            {
                var retries = 0;

                while(true)
                {
                    try
                    {
                        var req = await base.CreateRequestAsync(AUTH_URL, null, cancelToken);
                        req.Headers.Add("Authorization", string.Format("Basic {0}", m_credentials));
                        //req.ContentType = "application/json; charset=utf-8";
                        
                        using(var resp = await m_client.SendAsync(req, cancelToken))
                            m_config = await ReadJSONResponseAsync<AuthResponse>(resp, cancelToken);

                        m_config.APIUrl = DropTrailingSlashes(m_config.APIUrl);
                        m_config.DownloadUrl = DropTrailingSlashes(m_config.DownloadUrl);

                        m_configExpires = DateTime.UtcNow + TimeSpan.FromHours(1);
                        return m_config;
                    }
                    catch (Exception ex)
                    {
                        var clienterror = false;

                        try
                        {
                            // Only retry once on client errors
                            if (ex is HttpRequestStatusException exception)
                            {
                                var sc = (int)exception.Response.StatusCode;
                                clienterror = (sc >= 400 && sc <= 499);
                            }
                        }
                        catch
                        {
                        }

                        if (retries >= (clienterror ? 1 : 5))
                        {
                            await AttemptParseAndThrowExceptionAsync(ex);
                            throw;
                        }

                        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, retries)));
                        retries++;
                    }
                }
            }

            return m_config;
        }

        public static async Task AttemptParseAndThrowExceptionAsync(Exception ex)
        {
            Exception newex = null;
            try
            {
                if (ex is HttpRequestStatusException exception)
                {
                    var rawdata = await exception.Response.Content.ReadAsStringAsync();
                    newex = new Exception("Raw message: " + rawdata);

                    var msg = JsonConvert.DeserializeObject<ErrorResponse>(rawdata);
                    newex = new Exception(string.Format("{0} - {1}: {2}", msg.Status, msg.Code, msg.Message));
                }
            }
            catch
            {
            }

            if (newex != null)
                throw newex;
        }

        protected override Task ParseExceptionAsync(Exception ex)
            => AttemptParseAndThrowExceptionAsync(ex);

        public static HttpStatusCode GetExceptionStatusCode(Exception ex)
        {
            if (ex is HttpRequestStatusException exception)
                return exception.Response.StatusCode;
            else
                return default(HttpStatusCode);
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage requestMessage, CancellationToken cancelToken)
            => m_client.SendAsync(requestMessage, cancelToken);
            

        private class ErrorResponse
        {
            [JsonProperty("code")]
            public string Code { get; set; }
            [JsonProperty("message")]
            public string Message { get; set; }
            [JsonProperty("status")]
            public long Status { get; set; }

        }

        private class AuthResponse 
        {
            [JsonProperty("accountId")]
            public string AccountID { get; set; }
            [JsonProperty("apiUrl")]
            public string APIUrl { get; set; }
            [JsonProperty("authorizationToken")]
            public string AuthorizationToken { get; set; }
            [JsonProperty("downloadUrl")]
            public string DownloadUrl { get; set; }
        }

    }

}

