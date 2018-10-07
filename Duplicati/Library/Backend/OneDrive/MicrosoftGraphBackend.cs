using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

using Duplicati.Library.Backend.MicrosoftGraph;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

using Newtonsoft.Json;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Base class for all backends based on the Microsoft Graph API:
    /// https://developer.microsoft.com/en-us/graph/
    /// </summary>
    /// <remarks>
    /// HttpClient is used instead of OAuthHelper because OAuthHelper internally converts URLs to System.Uri, which throws UriFormatException when the URL contains ':' characters.
    /// https://stackoverflow.com/questions/2143856/why-does-colon-in-uri-passed-to-uri-makerelativeuri-cause-an-exception
    /// https://social.msdn.microsoft.com/Forums/vstudio/en-US/bf11fc74-975a-4c4d-8335-8e0579d17fdf/uri-containing-colons-incorrectly-throws-uriformatexception?forum=netfxbcl
    /// 
    /// Note that instead of using Task.Result to wait for the results of asynchronous operations,
    /// this class uses the Utility.Await() extension method, since it doesn't wrap exceptions in AggregateExceptions.
    /// </remarks>
    public abstract class MicrosoftGraphBackend : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
    {
        private const string SERVICES_AGREEMENT = "https://www.microsoft.com/en-us/servicesagreement";
        private const string PRIVACY_STATEMENT = "https://privacy.microsoft.com/en-us/privacystatement";

        private const string BASE_ADDRESS = "https://graph.microsoft.com";

        private const string AUTHID_OPTION = "authid";
        private const string UPLOAD_SESSION_FRAGMENT_SIZE_OPTION = "fragment-size";
        private const string UPLOAD_SESSION_FRAGMENT_RETRY_COUNT_OPTION = "fragment-retry-count";
        private const string UPLOAD_SESSION_FRAGMENT_RETRY_DELAY_OPTION = "fragment-retry-delay";

        private const int UPLOAD_SESSION_FRAGMENT_DEFAULT_RETRY_COUNT = 5;
        private const int UPLOAD_SESSION_FRAGMENT_DEFAULT_RETRY_DELAY = 1000;

        /// <summary>
        /// Max size of file that can be uploaded in a single PUT request is 4 MB:
        /// https://developer.microsoft.com/en-us/graph/docs/api-reference/v1.0/api/driveitem_put_content
        /// </summary>
        private const int PUT_MAX_SIZE = 4 * 1000 * 1000;

        /// <summary>
        /// Max size of each individual upload in an upload session is 60 MiB:
        /// https://docs.microsoft.com/en-us/onedrive/developer/rest-api/api/driveitem_createuploadsession
        /// </summary>
        private const int UPLOAD_SESSION_FRAGMENT_MAX_SIZE = 60 * 1024 * 1024;

        /// <summary>
        /// Default fragment size of 10 MiB, as the documentation recommends something in the range of 5-10 MiB,
        /// and it still complies with the 320 KiB multiple requirement.
        /// </summary>
        private const int UPLOAD_SESSION_FRAGMENT_DEFAULT_SIZE = 10 * 1024 * 1024;

        /// <summary>
        /// Each fragment in an upload session must be a size that is multiple of this size.
        /// https://docs.microsoft.com/en-us/onedrive/developer/rest-api/api/driveitem_createuploadsession
        /// There is some confusion in the docs as to whether this is actually required, however...
        /// </summary>
        private const int UPLOAD_SESSION_FRAGMENT_MULTIPLE_SIZE = 320 * 1024;

        private static readonly HttpMethod PatchMethod = new HttpMethod("PATCH");

        protected delegate string DescriptionTemplateDelegate(string mssadescription, string mssalink, string msopdescription, string msoplink);

        private readonly JsonSerializer m_serializer = new JsonSerializer();
        private readonly OAuthHttpClient m_client;
        private readonly int fragmentSize;
        private readonly int fragmentRetryCount;
        private readonly int fragmentRetryDelay; // In milliseconds

        private string[] dnsNames = null;

        private readonly Lazy<string> rootPathFromURL;
        private string RootPath => this.rootPathFromURL.Value;

        protected MicrosoftGraphBackend() { } // Constructor needed for dynamic loading to find it

        protected MicrosoftGraphBackend(string url, string protocolKey, Dictionary<string, string> options)
        {
            string authid;
            options.TryGetValue(AUTHID_OPTION, out authid);
            if (string.IsNullOrEmpty(authid))
                throw new UserInformationException(Strings.MicrosoftGraph.MissingAuthId(OAuthHelper.OAUTH_LOGIN_URL(protocolKey)), "MicrosoftGraphBackendMissingAuthId");

            string fragmentSizeStr;
            if (options.TryGetValue(UPLOAD_SESSION_FRAGMENT_SIZE_OPTION, out fragmentSizeStr) && int.TryParse(fragmentSizeStr, out this.fragmentSize))
            {
                // Make sure the fragment size is a multiple of the desired multiple size.
                // If it isn't, we round down to the nearest multiple below it.
                this.fragmentSize = (this.fragmentSize / UPLOAD_SESSION_FRAGMENT_MULTIPLE_SIZE) * UPLOAD_SESSION_FRAGMENT_MULTIPLE_SIZE;

                // Make sure the fragment size isn't larger than the maximum, or smaller than the minimum
                this.fragmentSize = Math.Max(Math.Min(this.fragmentSize, UPLOAD_SESSION_FRAGMENT_MAX_SIZE), UPLOAD_SESSION_FRAGMENT_MULTIPLE_SIZE);
            }
            else
            {
                this.fragmentSize = UPLOAD_SESSION_FRAGMENT_DEFAULT_SIZE;
            }

            string fragmentRetryCountStr;
            if (!(options.TryGetValue(UPLOAD_SESSION_FRAGMENT_RETRY_COUNT_OPTION, out fragmentRetryCountStr) && int.TryParse(fragmentRetryCountStr, out this.fragmentRetryCount)))
            {
                this.fragmentRetryCount = UPLOAD_SESSION_FRAGMENT_DEFAULT_RETRY_COUNT;
            }

            string fragmentRetryDelayStr;
            if (!(options.TryGetValue(UPLOAD_SESSION_FRAGMENT_RETRY_DELAY_OPTION, out fragmentRetryDelayStr) && int.TryParse(fragmentRetryDelayStr, out this.fragmentRetryDelay)))
            {
                this.fragmentRetryDelay = UPLOAD_SESSION_FRAGMENT_DEFAULT_RETRY_DELAY;
            }

            this.m_client = new OAuthHttpClient(authid, protocolKey);
            this.m_client.BaseAddress = new System.Uri(BASE_ADDRESS);

            // Extract out the path to the backup root folder from the given URI.  Since this can be an expensive operation, 
            // we will cache the value using a lazy initializer.
            this.rootPathFromURL = new Lazy<string>(() => this.GetRootPathFromUrl(url));
        }

        public abstract string ProtocolKey { get; }

        public abstract string DisplayName { get; }

        public string Description
        {
            get
            {
                return this.DescriptionTemplate(
                    "Microsoft Service Agreement",
                    SERVICES_AGREEMENT,
                    "Microsoft Online Privacy Statement", 
                    PRIVACY_STATEMENT); 
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new[]
                {
                    new CommandLineArgument(AUTHID_OPTION, CommandLineArgument.ArgumentType.Password, Strings.MicrosoftGraph.AuthIdShort, Strings.MicrosoftGraph.AuthIdLong(OAuthHelper.OAUTH_LOGIN_URL(this.ProtocolKey))),
                    new CommandLineArgument(UPLOAD_SESSION_FRAGMENT_SIZE_OPTION, CommandLineArgument.ArgumentType.Integer, Strings.MicrosoftGraph.FragmentSizeShort, Strings.MicrosoftGraph.FragmentSizeLong, Library.Utility.Utility.FormatSizeString(UPLOAD_SESSION_FRAGMENT_DEFAULT_SIZE)),
                    new CommandLineArgument(UPLOAD_SESSION_FRAGMENT_RETRY_COUNT_OPTION, CommandLineArgument.ArgumentType.Integer, Strings.MicrosoftGraph.FragmentRetryCountShort, Strings.MicrosoftGraph.FragmentRetryCountLong, UPLOAD_SESSION_FRAGMENT_DEFAULT_RETRY_COUNT.ToString()),
                    new CommandLineArgument(UPLOAD_SESSION_FRAGMENT_RETRY_DELAY_OPTION, CommandLineArgument.ArgumentType.Integer, Strings.MicrosoftGraph.FragmentRetryDelayShort, Strings.MicrosoftGraph.FragmentRetryDelayLong, UPLOAD_SESSION_FRAGMENT_DEFAULT_RETRY_DELAY.ToString()),
                }
                .Concat(this.AdditionalSupportedCommands).ToList();
            }
        }

        public string[] DNSName
        {
            get
            {
                if (this.dnsNames == null)
                {
                    // The DNS names that this instance may need to access include:
                    // - Core graph API endpoint
                    // - Upload session endpoint (which seems to be different depending on the drive being accessed - not sure if it can vary for a single drive)
                    // To get the upload session endpoint, we can start an upload session and then immediately cancel it.
                    // We pick a random file name (using a guid) to make sure we don't conflict with an existing file
                    string dnsTestFile = string.Format("DNSNameTest-{0}", Guid.NewGuid());
                    UploadSession uploadSession = this.Post<UploadSession>(string.Format("{0}/root:{1}{2}:/createUploadSession", this.DrivePrefix, this.RootPath, NormalizeSlashes(dnsTestFile)), null);

                    // Canceling an upload session is done by sending a DELETE to the upload URL
                    var request = new HttpRequestMessage(HttpMethod.Delete, uploadSession.UploadUrl);
                    var response = this.m_client.SendAsync(request).Await();
                    this.CheckResponse(response);

                    this.dnsNames = new[]
                        {
                            new System.Uri(BASE_ADDRESS).Host,
                            new System.Uri(uploadSession.UploadUrl).Host,
                        }
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }

                return this.dnsNames;
            }
        }

        public IQuotaInfo Quota
        {
            get
            {
                Drive driveInfo = this.Get<Drive>(this.DrivePrefix);
                if (driveInfo.Quota != null)
                {
                    // Some sources (SharePoint for example) seem to return 0 for these values even when the quota isn't exceeded..
                    // As a special test, if all the returned values are 0, we pretend that no quota was reported.
                    // This way we don't send spurious warnings because the quota looks like it is exceeded.
                    if (driveInfo.Quota.Total != 0 || driveInfo.Quota.Remaining != 0 || driveInfo.Quota.Used != 0)
                    {
                        return new QuotaInfo(driveInfo.Quota.Total, driveInfo.Quota.Remaining);
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Override-able fragment indicating the API version to use each query
        /// </summary>
        protected virtual string ApiVersion
        {
            get { return "/v1.0"; }
        }

        /// <summary>
        /// Normalized fragment (starting with a slash and ending without one) for the path to the drive to be used.
        /// For example: "/me/drive" for the default drive for a user.
        /// </summary>
        protected abstract string DrivePath { get; }

        protected abstract DescriptionTemplateDelegate DescriptionTemplate { get; }

        protected virtual IList<ICommandLineArgument> AdditionalSupportedCommands
        {
            get
            {
                return new ICommandLineArgument[0];
            }
        }

        private string DrivePrefix
        {
            get { return this.ApiVersion + this.DrivePath; }
        }

        public void CreateFolder()
        {
            string parentFolder = "root";
            string parentFolderPath = string.Empty;
            foreach (string folder in this.RootPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string nextPath = parentFolderPath + "/" + folder;
                DriveItem folderItem;
                try
                {
                    folderItem = this.Get<DriveItem>(string.Format("{0}/root:{1}", this.DrivePrefix, NormalizeSlashes(nextPath)));
                }
                catch (DriveItemNotFoundException)
                {
                    DriveItem newFolder = new DriveItem()
                    {
                        Name = folder,
                        Folder = new FolderFacet(),
                    };

                    folderItem = this.Post(string.Format("{0}/items/{1}/children", this.DrivePrefix, parentFolder), newFolder);
                }

                parentFolder = folderItem.Id;
                parentFolderPath = nextPath;
            }
        }

        public IEnumerable<IFileEntry> List()
        {
            try
            {
                return this.Enumerate<DriveItem>(string.Format("{0}/root:{1}:/children", this.DrivePrefix, this.RootPath))
                    .Where(item => item.IsFile && !item.IsDeleted) // Exclude non-files and deleted items (not sure if they show up in this listing, but make sure anyway)
                    .Select(item =>
                        new FileEntry(
                            item.Name,
                            item.Size ?? 0, // Files should always have a size, but folders don't need it
                            item.FileSystemInfo?.LastAccessedDateTime?.UtcDateTime ?? new DateTime(),
                            item.FileSystemInfo?.LastModifiedDateTime?.UtcDateTime ?? item.LastModifiedDateTime?.UtcDateTime ?? new DateTime()));
            }
            catch (DriveItemNotFoundException ex)
            {
                // If there's an 'item not found' exception here, it means the root folder didn't exist.
                throw new FolderMissingException(ex);
            }
        }

        public void Get(string remotename, string filename)
        {
            using (FileStream fileStream = File.OpenWrite(filename))
            {
                this.Get(remotename, fileStream);
            }
        }

        public void Get(string remotename, Stream stream)
        {
            try
            {
                var response = this.m_client.GetAsync(string.Format("{0}/root:{1}{2}:/content", this.DrivePrefix, this.RootPath, NormalizeSlashes(remotename))).Await();
                this.CheckResponse(response);
                using (Stream responseStream = response.Content.ReadAsStreamAsync().Await())
                {
                    responseStream.CopyTo(stream);
                }
            }
            catch (DriveItemNotFoundException ex)
            {
                // If the item wasn't found, wrap the exception so normal handling can occur.
                throw new FileMissingException(ex);
            }
        }

        public void Rename(string oldname, string newname)
        {
            try
            {
                this.Patch(string.Format("{0}/root:{1}{2}", this.DrivePrefix, this.RootPath, NormalizeSlashes(oldname)), new DriveItem() { Name = newname });
            }
            catch (DriveItemNotFoundException ex)
            {
                // If the item wasn't found, wrap the exception so normal handling can occur.
                throw new FileMissingException(ex);
            }
        }

        public void Put(string remotename, string filename)
        {
            using (FileStream fileStream = File.OpenRead(filename))
            {
                this.Put(remotename, fileStream);
            }
        }

        public void Put(string remotename, Stream stream)
        {
            // PUT only supports up to 4 MB file uploads. There's a separate process for larger files.
            if (stream.Length < PUT_MAX_SIZE)
            {
                StreamContent streamContent = new StreamContent(stream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                var response = this.m_client.PutAsync(string.Format("{0}/root:{1}{2}:/content", this.DrivePrefix, this.RootPath, NormalizeSlashes(remotename)), streamContent).Await();
                
                // Make sure this response is a valid drive item, though we don't actually use it for anything currently.
                this.ParseResponse<DriveItem>(response);
            }
            else
            {
                // This file is too large to be sent in a single request, so we need to send it in pieces in an upload session:
                // https://docs.microsoft.com/en-us/onedrive/developer/rest-api/api/driveitem_createuploadsession
                // The documentation seems somewhat contradictory - it states that uploads must be done sequentially,
                // but also states that the nextExpectedRanges value returned may indicate multiple ranges...
                // For now, this plays it safe and does a sequential upload.
                HttpRequestMessage createSessionRequest = new HttpRequestMessage(HttpMethod.Post, string.Format("{0}/root:{1}{2}:/createUploadSession", this.DrivePrefix, this.RootPath, NormalizeSlashes(remotename)));

                // Indicate that we want to replace any existing content with this new data we're uploading
                this.PrepareContent(new UploadSession() { Item = new DriveItem() { ConflictBehavior = ConflictBehavior.Replace } });

                HttpResponseMessage createSessionResponse = this.m_client.SendAsync(createSessionRequest).Await();
                UploadSession uploadSession = this.ParseResponse<UploadSession>(createSessionResponse);

                // If the stream's total length is less than the chosen fragment size, then we should make the buffer only as large as the stream.
                int bufferSize = (int)Math.Min(this.fragmentSize, stream.Length);

                byte[] fragmentBuffer = new byte[bufferSize];
                int read = 0;
                for (int offset = 0; offset < stream.Length; offset += read)
                {
                    read = stream.Read(fragmentBuffer, 0, bufferSize);

                    int retryCount = this.fragmentRetryCount;
                    for (int attempt = 0; attempt < retryCount; attempt++)
                    {
                        ByteArrayContent fragmentContent = new ByteArrayContent(fragmentBuffer, 0, read);
                        fragmentContent.Headers.ContentLength = read;
                        fragmentContent.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + read - 1, stream.Length);

                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, uploadSession.UploadUrl);
                        request.Content = fragmentContent;

                        HttpResponseMessage response = null;
                        try
                        {
                            // The uploaded put requests will error if they are authenticated
                            response = this.m_client.SendAsync(request, false).Await();

                            // Note: On the last request, the json result includes the default properties of the item that was uploaded
                            this.ParseResponse<UploadSession>(response);
                        }
                        catch (MicrosoftGraphException ex)
                        {
                            // Error handling based on recommendations here:
                            // https://docs.microsoft.com/en-us/onedrive/developer/rest-api/api/driveitem_createuploadsession#best-practices
                            if (attempt >= retryCount - 1)
                            {
                                // We've used up all our retry attempts
                                throw new UploadSessionException(createSessionResponse, offset / bufferSize, (int)Math.Ceiling((double)stream.Length / bufferSize), ex);
                            }
                            else if ((int)ex.Response.StatusCode >= 500 && (int)ex.Response.StatusCode < 600)
                            {
                                // If a 5xx error code is hit, we should use an exponential backoff strategy before retrying.
                                // To make things simpler, we just use the current attempt number as the exponential factor.
                                Thread.Sleep((int)Math.Pow(2, attempt) * this.fragmentRetryDelay); // If this is changed to use tasks, this should be changed to Task.Await()
                                continue;
                            }
                            else if (ex.Response.StatusCode == HttpStatusCode.NotFound)
                            {
                                // 404 is a special case indicating the upload session no longer exists, so the fragment shouldn't be retried.
                                // Instead we'll let the caller re-attempt the whole file.
                                throw new UploadSessionException(createSessionResponse, offset / bufferSize, (int)Math.Ceiling((double)stream.Length / bufferSize), ex);
                            }
                            else if ((int)ex.Response.StatusCode >= 400 && (int)ex.Response.StatusCode < 500)
                            {
                                // If a 4xx error code is hit, we should retry without the backoff attempt
                                continue;
                            }
                            else
                            {
                                // Other errors should be rethrown
                                throw new UploadSessionException(createSessionResponse, offset / bufferSize, (int)Math.Ceiling((double)stream.Length / bufferSize), ex);
                            }
                        }

                        // If we successfully sent this piece, then we can break out of the retry loop
                        break;
                    }
                }
            }
        }

        public void Delete(string remotename)
        {
            var response = this.m_client.DeleteAsync(string.Format("{0}/root:{1}{2}", this.DrivePrefix, this.RootPath, NormalizeSlashes(remotename))).Await();
            try
            {
                this.CheckResponse(response);
            }
            catch (DriveItemNotFoundException ex)
            {
                // Wrap the existing item not found error in a 'FolderMissingException'
                throw new FileMissingException(ex);
            }
        }

        public void Test()
        {
            try
            {
                string rootPath = string.Format("{0}/root:{1}", this.DrivePrefix, this.RootPath);
                this.Get<DriveItem>(rootPath);
            }
            catch (DriveItemNotFoundException ex)
            {
                // Wrap the existing item not found error in a 'FolderMissingException'
                throw new FolderMissingException(ex);
            }
        }

        public void Dispose()
        {
            if (this.m_client != null)
            {
                this.m_client.Dispose();
            }
        }

        protected virtual string GetRootPathFromUrl(string url)
        {
            // Extract out the path to the backup root folder from the given URI
            var uri = new Utility.Uri(url);

            return Utility.Uri.UrlDecode(uri.HostAndPath);
        }

        protected T Get<T>(string url)
        {
            return this.SendRequest<T>(HttpMethod.Get, url);
        }

        protected T Post<T>(string url, T body)
        {
            return this.SendRequest(HttpMethod.Post, url, body);
        }

        protected T Patch<T>(string url, T body)
        {
            return this.SendRequest(PatchMethod, url, body);
        }

        private T SendRequest<T>(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            return this.SendRequest<T>(request);
        }

        private T SendRequest<T>(HttpMethod method, string url, T body)
        {
            var request = new HttpRequestMessage(method, url);
            if (!Object.Equals(body, default(T)))
            {
                request.Content = this.PrepareContent(body);
            }

            return this.SendRequest<T>(request);
        }

        private T SendRequest<T>(HttpRequestMessage request)
        {
            var response = this.m_client.SendAsync(request).Await();
            return this.ParseResponse<T>(response);
        }

        private IEnumerable<T> Enumerate<T>(string url)
        {
            string nextUrl = url;
            while (!string.IsNullOrEmpty(nextUrl))
            {
                GraphCollection<T> results = this.Get<GraphCollection<T>>(nextUrl);
                foreach (T result in results.Value)
                {
                    yield return result;
                }

                nextUrl = results.ODataNextLink;
            }
        }

        private void CheckResponse(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // It looks like this is an 'item not found' exception, so wrap it in a new exception class to make it easier to pick things out.
                    throw new DriveItemNotFoundException(response);
                }
                else
                {
                    // Throw a wrapper exception to make it easier for the caller to look at specific status codes, etc.
                    throw new MicrosoftGraphException(response);
                }
            }
        }

        private T ParseResponse<T>(HttpResponseMessage response)
        {
            this.CheckResponse(response);
            using (Stream responseStream = response.Content.ReadAsStreamAsync().Await())
            using (StreamReader reader = new StreamReader(responseStream))
            using (JsonTextReader jsonReader = new JsonTextReader(reader))
            {
                return this.m_serializer.Deserialize<T>(jsonReader);
            }
        }

        /// <summary>
        /// Normalizes the slashes in a url fragment. For example:
        ///   "" => ""
        ///   "test" => "/test"
        ///   "test/" => "/test"
        ///   "a\b" => "/a/b"
        /// </summary>
        /// <param name="url">Url fragment to normalize</param>
        /// <returns>Normalized fragment</returns>
        private static string NormalizeSlashes(string url)
        {
            url = url.Replace('\\', '/');

            if (url.Length != 0 && !url.StartsWith("/", StringComparison.Ordinal))
                url = "/" + url;

            if (url.EndsWith("/", StringComparison.Ordinal))
                url = url.Substring(0, url.Length - 1);

            return url;
        }

        private StringContent PrepareContent<T>(T body)
        {
            return new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
        }
    }
}
