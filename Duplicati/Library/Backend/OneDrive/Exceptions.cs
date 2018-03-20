using System;
using System.Net.Http;

using Duplicati.Library.Utility;

using Newtonsoft.Json;

namespace Duplicati.Library.Backend.MicrosoftGraph
{
    public class MicrosoftGraphException : Exception
    {
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
            this.Response = response;
        }

        public string RequestUrl => this.Response.RequestMessage.RequestUri.ToString();
        public HttpResponseMessage Response { get; private set; }

        protected static string ResponseToString(HttpResponseMessage response)
        {
            if (response != null)
            {
                string content = response.Content.ReadAsStringAsync().Await();
                return string.Format("{0}\n{1}\n{2}", response.RequestMessage, response, JsonConvert.SerializeObject(JsonConvert.DeserializeObject(content), Formatting.Indented));
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
    }

    public class DriveItemNotFoundException : MicrosoftGraphException
    {
        public DriveItemNotFoundException(HttpResponseMessage response)
            : base(string.Format("Item at {0} was not found", response?.RequestMessage?.RequestUri?.ToString() ?? "<unknown>"), response)
        {
        }
    }

    public class UploadSessionException : MicrosoftGraphException
    {
        public UploadSessionException(
            HttpResponseMessage originalResponse,
            int fragment,
            int fragmentCount,
            MicrosoftGraphException fragmentException)
            : base(
                  string.Format("Error uploading fragment {0} of {1} for {2}", fragment, fragmentCount, originalResponse?.RequestMessage?.RequestUri?.ToString() ?? "<unknown>"),
                  originalResponse,
                  fragmentException)
        {
            this.Fragment = fragment;
            this.FragmentCount = fragmentCount;
            this.InnerException = fragmentException;
        }

        public string CreateSessionRequestUrl => this.RequestUrl;
        public HttpResponseMessage CreateSessionResponse => this.Response;

        public int Fragment { get; private set; }
        public int FragmentCount { get; private set; }
        public string FragmentRequestUrl => this.InnerException.RequestUrl;
        public HttpResponseMessage FragmentResponse => this.InnerException.Response;

        public new MicrosoftGraphException InnerException { get; private set; }
    }
}
