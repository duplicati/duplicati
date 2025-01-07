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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Duplicati.Library.Utility;

using Newtonsoft.Json;

namespace Duplicati.Library.Backend.MicrosoftGraph
{
    public class MicrosoftGraphException : Exception
    {
        private static readonly Regex authorizationHeaderRemover = new Regex(@"Authorization:\s*Bearer\s+\S+", RegexOptions.IgnoreCase);

        private readonly HttpResponseMessage responseMessage;
        private readonly HttpWebResponse webResponse;

        public MicrosoftGraphException(HttpResponseMessage response)
            : this(string.Format("{0}: {1} error from request {2}", response.StatusCode, response.ReasonPhrase, response.RequestMessage.RequestUri), response)
        {
        }

        public MicrosoftGraphException(string message, HttpResponseMessage response)
            : this(message, response, null)
        {
        }

        public MicrosoftGraphException(string message, HttpResponseMessage response, Exception innerException)
            : base(BuildFullMessage(message, response), innerException)
        {
            this.responseMessage = response;
        }

        public MicrosoftGraphException(HttpWebResponse response)
            : this(string.Format("{0}: {1} error from request {2}", response.StatusCode, response.StatusDescription, response.ResponseUri), response)
        {
        }

        public MicrosoftGraphException(string message, HttpWebResponse response)
            : this(message, response, null)
        {
        }

        public MicrosoftGraphException(string message, HttpWebResponse response, Exception innerException)
            : base(BuildFullMessage(message, response), innerException)
        {
            this.webResponse = response;
        }

        public string RequestUrl
        {
            get
            {
                if (this.responseMessage != null)
                {
                    return this.responseMessage.RequestMessage.RequestUri.ToString();
                }
                else
                {
                    return this.webResponse.ResponseUri.ToString();
                }
            }
        }

        public HttpStatusCode StatusCode
        {
            get
            {
                if (this.responseMessage != null)
                {
                    return this.responseMessage.StatusCode;
                }
                else
                {
                    return this.webResponse.StatusCode;
                }
            }
        }

        protected static string ResponseToString(HttpResponseMessage response)
        {
            if (response != null)
            {
                // Start to read the content
                using (Task<string> content = response.Content.ReadAsStringAsync())
                {
                    // Since the exception message may be saved / sent in logs, we want to prevent the authorization header from being included.
                    // it wouldn't be as bad as recording the username/password in logs, since the token will expire, but it doesn't hurt to be safe.
                    // So we replace anything in the request that looks like the auth header with a safe version.
                    string requestMessage = authorizationHeaderRemover.Replace(response.RequestMessage.ToString(), "Authorization: Bearer ABC...XYZ");
                    return string.Format("{0}\n{1}\n{2}", requestMessage, response, PrettifyJson(content.Await()));
                }
            }
            else
            {
                return null;
            }
        }

        private static string BuildFullMessage(string message, HttpResponseMessage response)
        {
            if (response != null)
            {
                return string.Format("{0}\n{1}", message, ResponseToString(response));
            }
            else
            {
                return message;
            }
        }

        protected static string ResponseToString(HttpWebResponse response)
        {
            if (response != null)
            {
                // Start to read the content
                using (var responseStream = response.GetResponseStream())
                using (var textReader = new StreamReader(responseStream))
                {
                    return string.Format("{0}\n{1}", response, PrettifyJson(textReader.ReadToEnd()));
                }
            }
            else
            {
                return null;
            }
        }

        private static string BuildFullMessage(string message, HttpWebResponse response)
        {
            if (response != null)
            {
                return string.Format("{0}\n{1}", message, ResponseToString(response));
            }
            else
            {
                return message;
            }
        }

        private static string PrettifyJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            if (json[0] == '<')
            {
                // It looks like some errors return xml bodies instead of JSON.
                // If this looks like it might be one of those, don't even bother parsing the JSON.
                return json;
            }

            try
            {
                return JsonConvert.SerializeObject(JsonConvert.DeserializeObject(json), Formatting.Indented);
            }
            catch (Exception)
            {
                // Maybe this wasn't JSON..
                return json;
            }
        }
    }

    public class DriveItemNotFoundException : MicrosoftGraphException
    {
        public DriveItemNotFoundException(HttpResponseMessage response)
            : base(string.Format("Item at {0} was not found", response?.RequestMessage?.RequestUri?.ToString() ?? "<unknown>"), response)
        {
        }

        public DriveItemNotFoundException(HttpWebResponse response)
            : base(string.Format("Item at {0} was not found", response?.ResponseUri?.ToString() ?? "<unknown>"), response)
        {
        }
    }

    public class UploadSessionException : MicrosoftGraphException
    {
        public UploadSessionException(
            HttpResponseMessage originalResponse,
            int fragment,
            int fragmentCount,
            Exception fragmentException)
            : base(
                  string.Format("Error uploading fragment {0} of {1} for {2}", fragment, fragmentCount, originalResponse?.RequestMessage?.RequestUri?.ToString() ?? "<unknown>"),
                  originalResponse,
                  fragmentException)
        {
            this.Fragment = fragment;
            this.FragmentCount = fragmentCount;
        }

        public UploadSessionException(
            HttpWebResponse originalResponse,
            int fragment,
            int fragmentCount,
            Exception fragmentException)
            : base(
                  string.Format("Error uploading fragment {0} of {1} for {2}", fragment, fragmentCount, originalResponse?.ResponseUri?.ToString() ?? "<unknown>"),
                  originalResponse,
                  fragmentException)
        {
            this.Fragment = fragment;
            this.FragmentCount = fragmentCount;
        }

        public int Fragment { get; private set; }
        public int FragmentCount { get; private set; }
    }
}
