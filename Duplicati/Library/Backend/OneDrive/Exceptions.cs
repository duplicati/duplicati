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
using System.Net;
using System.Text.RegularExpressions;

using Duplicati.Library.Utility;

using Newtonsoft.Json;

namespace Duplicati.Library.Backend.MicrosoftGraph
{
    public class MicrosoftGraphException : Exception
    {
        private static readonly Regex authorizationHeaderRemover = new Regex(@"Authorization:\s*Bearer\s+\S+", RegexOptions.IgnoreCase);

        private readonly HttpResponseMessage? responseMessage;

        public MicrosoftGraphException(HttpResponseMessage response)
            : this(string.Format("{0}: {1} error from request {2}", response.StatusCode, response.ReasonPhrase, response.RequestMessage?.RequestUri), response)
        {
        }

        public MicrosoftGraphException(string message, HttpResponseMessage response)
            : this(message, response, null)
        {
        }

        public MicrosoftGraphException(string message, HttpResponseMessage response, string? content)
            : this(message, response, content, null)
        {
        }

        public MicrosoftGraphException(string message, HttpResponseMessage response, string? content, Exception? innerException)
            : base(BuildFullMessage(message, response, content), innerException)
        {
            this.responseMessage = response;
        }

        public HttpStatusCode StatusCode
        {
            get
            {
                if (responseMessage != null)
                    return responseMessage.StatusCode;
                return HttpStatusCode.InternalServerError;
            }
        }

        protected static string? ResponseToString(HttpResponseMessage response, string? content)
        {
            if (response == null)
                return null;

            try
            {
                // Try to read the contents, but not if we already have it
                if (string.IsNullOrEmpty(content))
                    content = PrettifyJson(response.Content.ReadAsStringAsync().Await());
            }
            catch (Exception ex)
            {
                content = $"<error reading body>: {ex.Message}";
            }

            // Prevent leaking any authorization headers
            var requestMessage = response.RequestMessage == null
                ? ""
                : authorizationHeaderRemover.Replace(response.RequestMessage.ToString(), "Authorization: Bearer ABC...XYZ");
            return string.Format("{0}\n{1}\n{2}", requestMessage, response, content);

        }

        private static string BuildFullMessage(string message, HttpResponseMessage response, string? content)
        {
            if (response != null)
            {
                return string.Format("{0}\n{1}", message, ResponseToString(response, content));
            }
            else
            {
                return message;
            }
        }

        private static string PrettifyJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

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
            : base(string.Format("Item at {0} was not found", response.RequestMessage?.RequestUri?.ToString() ?? "<unknown>"), response)
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
                  string.Format("Error uploading fragment {0} of {1} for {2}", fragment, fragmentCount, originalResponse.RequestMessage?.RequestUri?.ToString() ?? "<unknown>"),
                  originalResponse,
                  null,
                  fragmentException)
        {
            this.Fragment = fragment;
            this.FragmentCount = fragmentCount;
        }

        public int Fragment { get; private set; }
        public int FragmentCount { get; private set; }
    }
}
