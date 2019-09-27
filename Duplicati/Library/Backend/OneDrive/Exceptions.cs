using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Duplicati.Library.Utility;

using Newtonsoft.Json;

namespace Duplicati.Library.Backend.MicrosoftGraph
{
    public class MicrosoftGraphException : Exception
    {
        private static readonly Regex authorizationHeaderRemover = new Regex(@"Authorization:\s*Bearer\s+\S+", RegexOptions.IgnoreCase);

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
                // Start to read the content
                Task<string> content = response.Content.ReadAsStringAsync();

                // Since the exception message may be saved / sent in logs, we want to prevent the authorization header from being included.
                // it wouldn't be as bad as recording the username/password in logs, since the token will expire, but it doesn't hurt to be safe.
                // So we replace anything in the request that looks like the auth header with a safe version.
                string requestMessage = authorizationHeaderRemover.Replace(response.RequestMessage.ToString(), "Authorization: Bearer ABC...XYZ");
                return string.Format("{0}\n{1}\n{2}", requestMessage, response, JsonConvert.SerializeObject(JsonConvert.DeserializeObject(content.Await()), Formatting.Indented));
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
