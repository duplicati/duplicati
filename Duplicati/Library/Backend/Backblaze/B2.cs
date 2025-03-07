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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Duplicati.Library.Backend.Backblaze.Model;
using FileEntry = Duplicati.Library.Common.IO.FileEntry;
using System.Runtime.CompilerServices;
using Backblaze;

namespace Duplicati.Library.Backend.Backblaze;

/// <summary>
/// Backblaze B2 Backend
/// </summary>
public class B2 : IStreamingBackend, ITimeoutExemptBackend, IStateEnabledModule<B2SharedState>
{
    /// <summary>
    /// The default timeout in seconds for LIST/CreateFolder operations
    /// </summary>
    private const int SHORT_OPERATION_TIMEOUT_SECONDS = 30;

    /// <summary>
    /// The option key for specifying the Backblaze B2 account ID
    /// </summary>
    private const string B2_ID_OPTION = "b2-accountid";

    /// <summary>
    /// The option key for specifying the Backblaze B2 application key
    /// </summary>
    private const string B2_KEY_OPTION = "b2-applicationkey";

    /// <summary>
    /// The option key for specifying the number of files to retrieve per API request
    /// </summary>
    private const string B2_PAGESIZE_OPTION = "b2-page-size";

    /// <summary>
    /// The option key for specifying a custom download URL for the B2 service
    /// </summary>
    private const string B2_DOWNLOAD_URL_OPTION = "b2-download-url";

    /// <summary>
    /// The option key for specifying the bucket type when creating new buckets
    /// </summary>
    private const string B2_CREATE_BUCKET_TYPE_OPTION = "b2-create-bucket-type";

    /// <summary>
    /// The default bucket type for new buckets - set to private access
    /// </summary>
    private const string DEFAULT_BUCKET_TYPE = "allPrivate";

    /// <summary>
    /// The default number of files to retrieve per API request
    /// </summary>
    private const int DEFAULT_PAGE_SIZE = 500;

    /// <summary>
    /// Recommended chunk size as per Backblaze B2 documentation (100MB)
    /// </summary>
    private const int B2_RECOMMENDED_CHUNK_SIZE = 100 * 1024 * 1024;

    /// <summary>
    /// Default retry-after time in seconds for B2 API requests
    /// </summary>
    private const int B2_RETRY_AFTER_SECONDS = 5;

    /// <summary>
    /// The name of the B2 bucket being accessed
    /// </summary>
    private readonly string _bucketName;

    /// <summary>
    /// The path prefix for all operations within the bucket
    /// </summary>
    private readonly string _prefix;

    /// <summary>
    /// URL-encoded version of the path prefix for API requests
    /// </summary>
    private readonly string _urlencodedPrefix;

    /// <summary>
    /// The type of bucket (e.g., allPrivate, allPublic) being accessed or created
    /// </summary>
    private readonly string _bucketType;

    /// <summary>
    /// The number of files to retrieve per API request
    /// </summary>
    private readonly int _pageSize;

    /// <summary>
    /// Custom download URL for the B2 service, if specified
    /// </summary>
    private readonly string _downloadUrl;

    /// <summary>
    /// Cached upload URL and authorization token for file uploads
    /// </summary>
    private UploadUrlResponse _uploadUrl;

    /// <summary>
    /// The current bucket entity containing bucket information and credentials
    /// </summary>
    private BucketEntity _bucket;

    /// <summary>
    /// The shared state, if multiple instances are running
    /// </summary>
    private B2SharedState _sharedState;

    /// <summary>
    /// Cache lock for BucketEntity
    /// </summary>
    private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

    /// <summary>
    /// The B2AuthState for creating a new B2AuthHelper
    /// </summary>
    private readonly (string AccountId, string AccountKey) _b2AuthState;

    /// <summary>
    /// Empty constructor is required for the backend to be loaded by the backend factory
    /// </summary>
    public B2()
    {
    }

    /// <summary>
    /// Actual constructor for the backend that accepts the url and options
    /// </summary>
    /// <param name="url">URL in Duplicati Uri format</param>
    /// <param name="options">options to be used in the backend</param>
    public B2(string url, Dictionary<string, string> options)
    {
        var uri = new Utility.Uri(url);

        _bucketName = uri.Host;
        _prefix = Util.AppendDirSeparator("/" + uri.Path, "/");

        // For B2 we do not use a leading slash
        while (_prefix.StartsWith("/", StringComparison.Ordinal))
            _prefix = _prefix.Substring(1);

        _urlencodedPrefix = string.Join("/", _prefix.Split(new[] { '/' }).Select(x => Utility.Uri.UrlPathEncode(x)));

        _bucketType = DEFAULT_BUCKET_TYPE;
        if (options.TryGetValue(B2_CREATE_BUCKET_TYPE_OPTION, out var option1))
            _bucketType = option1;

        string accountId = null;
        string accountKey = null;

        // Takes the account ID and key from the options, or from the URL with the cascading precedence

        if (options.TryGetValue("auth-username", out var authUsernameOption))
            accountId = authUsernameOption;
        if (options.TryGetValue("auth-password", out var authPasswordOption))
            accountKey = authPasswordOption;
        if (options.TryGetValue(B2_ID_OPTION, out var accountIdOption))
            accountId = accountIdOption;
        if (options.TryGetValue(B2_KEY_OPTION, out var accountKeyOption))
            accountKey = accountKeyOption;
        if (!string.IsNullOrEmpty(uri.Username))
            accountId = uri.Username;
        if (!string.IsNullOrEmpty(uri.Password))
            accountKey = uri.Password;

        if (string.IsNullOrEmpty(accountId))
            throw new UserInformationException(Strings.B2.NoB2UserIDError, "B2MissingUserID");

        if (string.IsNullOrEmpty(accountKey))
            throw new UserInformationException(Strings.B2.NoB2KeyError, "B2MissingKey");

        _pageSize = DEFAULT_PAGE_SIZE;
        if (options.ContainsKey(B2_PAGESIZE_OPTION))
        {
            int.TryParse(options[B2_PAGESIZE_OPTION], out _pageSize);

            if (_pageSize <= 0)
                throw new UserInformationException(Strings.B2.InvalidPageSizeError(B2_PAGESIZE_OPTION, options[B2_PAGESIZE_OPTION]), "B2InvalidPageSize");
        }

        _downloadUrl = null;
        if (options.TryGetValue(B2_DOWNLOAD_URL_OPTION, out var option)) _downloadUrl = option;

        _b2AuthState = (accountId, accountKey);
    }

    /// <summary>
    /// Lock object for shared state
    /// </summary>
    private readonly object _sharedStateLock = new object();

    /// <summary>
    /// Indicates whether the backend owns the shared state
    /// </summary>
    private bool _ownSharedState = false;

    /// <summary>
    /// Gets the shared state for the backend
    /// </summary>
    private B2SharedState SharedState
    {
        get
        {
            lock (_sharedStateLock)
            {
                if (_sharedState != null)
                    return _sharedState;

                _sharedState = new B2SharedState();
                _ownSharedState = true;
                return _sharedState;
            }
        }
    }

    /// <summary>
    /// Gets or creates the B2AuthHelper for the backend
    /// </summary>
    private B2AuthHelper AuthHelper
    {
        get
        {
            B2AuthHelper authHelper = null;
            SharedState.RentState(x =>
            {
                if (x.AuthHelper == null)
                    x = x with { AuthHelper = new B2AuthHelper(_b2AuthState.AccountId, _b2AuthState.AccountKey) };

                authHelper = x.AuthHelper;
                return x;
            });

            return authHelper;
        }
    }

    /// <summary>
    /// Retrieves the bucket list from the B2 service using the account ID
    /// and searchs for a match for the configured bucket name.
    ///
    /// If not found, throws a FolderMissingException.
    ///
    /// Caches the result in the _mBucket field.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FolderMissingException"></exception>
    private async Task<BucketEntity> GetBucketAsync(CancellationToken cancellationToken)
    {
        if (_bucket != null) return _bucket;

        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var timeoutToken = new CancellationTokenSource();
            timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));
            using var combinedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancellationToken);

            var buckets = await AuthHelper.PostAndGetJsonDataAsync<ListBucketsResponse>(
                $"{AuthHelper.ApiUrl}/b2api/v1/b2_list_buckets",
                new ListBucketsRequest(accountId: AuthHelper.AccountId),
                combinedCancellationToken.Token).ConfigureAwait(false);

            return _bucket ??= buckets?.Buckets?.FirstOrDefault(x =>
                                    string.Equals(x.BucketName, _bucketName, StringComparison.OrdinalIgnoreCase))
                                ?? throw new FolderMissingException();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Retrieves the upload URL and authorization token for the current bucket.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    private async Task<UploadUrlResponse> GetUploadUrlDataAsync(CancellationToken cancellationToken)
    {
        if (_uploadUrl != null)
            return _uploadUrl;

        using var timeoutToken = new CancellationTokenSource();
        timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));
        using var combinedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancellationToken);

        _uploadUrl = await AuthHelper.PostAndGetJsonDataAsync<UploadUrlResponse>(
            $"{AuthHelper.ApiUrl}/b2api/v1/b2_get_upload_url",
            new UploadUrlRequest { BucketID = GetBucketAsync(cancellationToken).Await().BucketID },
            combinedCancellationToken.Token
        ).ConfigureAwait(false);

        return _uploadUrl;
    }

    /// <summary>
    /// Gets the file entry for the specified filename, or null if not found.
    /// </summary>
    /// <param name="filename">The filename to locate</param>
    /// <param name="cancelToken">The cancellation token</param>
    /// <returns>The file entry, or null if not found</returns>
    private async Task<FileEntity> GetFileEntity(string filename, CancellationToken cancelToken)
    {
        var res = await SharedState.GetFileEntityFromCache(filename).ConfigureAwait(false);
        if (res != null)
            return res;

        await RebuildFileCache(true, cancelToken).ConfigureAwait(false);

        return await SharedState.GetFileEntityFromCache(filename).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the fileID searching by filename.
    /// </summary>
    /// <param name="filename">Filename</param>
    /// <param name="cancelToken">Cancellation Token</param>
    /// <returns></returns>
    /// <exception cref="FileMissingException">Exception thrown when file is not found</exception>
    private async Task<string> GetFileId(string filename, CancellationToken cancelToken)
        => (await GetFileEntity(filename, cancelToken).ConfigureAwait(false)
            ?? throw new FileMissingException()).FileID;

    /// <summary>
    /// Returns the DownloadURL, either cached, or making a call to the server's /b2_authorize_account
    /// </summary>
    private string DownloadUrl => string.IsNullOrEmpty(_downloadUrl) ? AuthHelper.DownloadUrl : _downloadUrl;

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands =>
        new List<ICommandLineArgument>([
            new CommandLineArgument(B2_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.B2.B2accountidDescriptionShort, Strings.B2.B2accountidDescriptionLong, null,
                ["auth-password"], null),
            new CommandLineArgument(B2_KEY_OPTION, CommandLineArgument.ArgumentType.Password, Strings.B2.B2applicationkeyDescriptionShort, Strings.B2.B2applicationkeyDescriptionLong, null,
                ["auth-username"], null),
            new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.B2.AuthPasswordDescriptionShort, Strings.B2.AuthPasswordDescriptionLong),
            new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.B2.AuthUsernameDescriptionShort, Strings.B2.AuthUsernameDescriptionLong),
            new CommandLineArgument(B2_CREATE_BUCKET_TYPE_OPTION, CommandLineArgument.ArgumentType.String, Strings.B2.B2createbuckettypeDescriptionShort, Strings.B2.B2createbuckettypeDescriptionLong, DEFAULT_BUCKET_TYPE),
            new CommandLineArgument(B2_PAGESIZE_OPTION, CommandLineArgument.ArgumentType.Integer, Strings.B2.B2pagesizeDescriptionShort, Strings.B2.B2pagesizeDescriptionLong, DEFAULT_PAGE_SIZE.ToString()),
            new CommandLineArgument(B2_DOWNLOAD_URL_OPTION, CommandLineArgument.ArgumentType.String, Strings.B2.B2downloadurlDescriptionShort, Strings.B2.B2downloadurlDescriptionLong)
        ]);

    public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        TempFile tmp = null;

        var measure = stream;
        while (measure is OverrideableStream os &&
               os.GetType().GetField("m_basestream", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(os) is Stream baseStream)
        {
            measure = baseStream;
        }

        if (measure == null)
            throw new Exception($"Unable to unwrap stream from: {stream.GetType()}");

        string sha1;
        if (measure.CanSeek)
        {
            var p = measure.Position;

            // Compute the hash
            using (var hashalg = HashFactory.CreateHasher("SHA1"))
                sha1 = Library.Utility.Utility.ByteArrayAsHexString(hashalg.ComputeHash(measure));

            measure.Position = p;
        }
        else
        {
            // No seeking possible, use a temp file
            tmp = new TempFile();
            await using (var sr = File.OpenWrite(tmp))
            using (var hasher = HashFactory.CreateHasher("SHA1"))
            await using (var hc = new HashCalculatingStream(measure, hasher))
            {
                await Utility.Utility.CopyStreamAsync(hc, sr, cancelToken).ConfigureAwait(false);
                sha1 = hc.GetFinalHashString();
            }

            stream = File.OpenRead(tmp);
        }

        await RebuildFileCache(false, cancelToken).ConfigureAwait(false);
        var oldEntry = await SharedState.GetFileEntityFromCache(remotename).ConfigureAwait(false);

        var uploadUrlData = await GetUploadUrlDataAsync(cancelToken).ConfigureAwait(false);
        try
        {

            // For PutAsync, no timeout is specified. The only thing that can stop it is the cancellation token
            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrlData.UploadUrl);

            request.Headers.TryAddWithoutValidation("Authorization", uploadUrlData.AuthorizationToken);
            request.Headers.Add("X-Bz-Content-Sha1", sha1);
            request.Headers.Add("X-Bz-File-Name", _urlencodedPrefix + Utility.Uri.UrlPathEncode(remotename));
            request.Content = new StreamContent(stream, B2_RECOMMENDED_CHUNK_SIZE);

            request.Content.Headers.Add("Content-Type", "application/octet-stream");
            request.Content.Headers.Add("Content-Length", stream.Length.ToString());

            var response = await AuthHelper.HttpClient.UploadStream(request, cancelToken).ConfigureAwait(false);
            var rdata = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);

            UploadFileResponse fileinfo;
            using (var tr = new StreamReader(rdata))
            await using (var jr = new Newtonsoft.Json.JsonTextReader(tr))
                fileinfo = new Newtonsoft.Json.JsonSerializer().Deserialize<UploadFileResponse>(jr);


            // Delete old versions
            if (oldEntry != null)
                await DeleteAsync(remotename, cancelToken).ConfigureAwait(false);

            await SharedState.RentStateAsync(cache =>
            {
                if (cache.FileCache != null)
                    cache.FileCache[remotename] = [
                        new FileEntity {
                            FileID = fileinfo.FileID,
                            FileName = fileinfo.FileName,
                            Action = "upload",
                            Size = fileinfo.ContentLength,
                            UploadTimestamp = (long)(DateTime.UtcNow - Utility.Utility.EPOCH).TotalMilliseconds
                        }];

                return cache;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SharedState.ClearFileCache().ConfigureAwait(false);
            var code = (int)B2AuthHelper.GetExceptionStatusCode(ex);
            if (code is >= 500 and <= 599)
                _uploadUrl = null;

            throw;
        }
        finally
        {
            tmp?.Dispose();
        }
    }

    /// <summary>
    /// Download files from remote
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="stream">Destination stream to write to</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution or FileMissingException</exception>
    public async Task GetAsync(string remotename, Stream stream, CancellationToken cancellationToken)
    {
        var fileentry = await GetFileEntity(remotename, cancellationToken).ConfigureAwait(false);

        using var request = fileentry != null
            ? AuthHelper.CreateRequest(
                $"{DownloadUrl}/b2api/v1/b2_download_file_by_id?fileId={Utility.Uri.UrlEncode(fileentry.FileID)}")
            : AuthHelper.CreateRequest(
                $"{DownloadUrl}/{_urlencodedPrefix}{Utility.Uri.UrlPathEncode(remotename)}");

        HttpResponseMessage response = null;
        try
        {
            // For GetAsync, no timeout is specified. The only thing that can stop it is the cancellation token
            response = await AuthHelper.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await responseStream.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (B2AuthHelper.GetExceptionStatusCode(ex) == HttpStatusCode.NotFound)
                throw new FileMissingException();

            AuthHelper.AttemptParseAndThrowException(ex, response);
            throw;
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>
    /// Implementation of interface method for listing remote folder contents
    /// </summary>
    /// <param name="force">Force a rebuild of the file cache</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <param name="updatedStateCallback">Callback to update the shared state</param>
    /// <returns>List of IFileEntry with directory listing result</returns>
    private async Task RebuildFileCache(bool force, CancellationToken cancellationToken, Action<B2SharedState.Values> updatedStateCallback = null)
    {
        await SharedState.RentStateAsync(async prev =>
        {
            if (prev.FileCache != null && !force)
                return prev;

            if (prev.AuthHelper == null)
                prev = prev with { AuthHelper = new B2AuthHelper(_b2AuthState.AccountId, _b2AuthState.AccountKey) };

            var cache = new Dictionary<string, List<FileEntity>>();
            string nextFileId = null;
            string nextFileName = null;
            string listVersionUrl = $"{prev.AuthHelper.ApiUrl}/b2api/v1/b2_list_file_versions";

            var listRetryHelper = RetryAfterHelper.CreateOrGetRetryAfterHelper(listVersionUrl);

            do
            {
                try
                {
                    using var timeoutToken = new CancellationTokenSource();
                    timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));
                    using var combinedCancellationToken =
                        CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancellationToken);

                    var resp = await prev.AuthHelper.PostAndGetJsonDataAsync<ListFilesResponse>(
                        $"{prev.AuthHelper.ApiUrl}/b2api/v1/b2_list_file_versions",
                        new ListFilesRequest
                        {
                            BucketID = GetBucketAsync(cancellationToken).Await().BucketID,
                            MaxFileCount = _pageSize,
                            Prefix = _prefix,
                            StartFileID = nextFileId,
                            StartFileName = nextFileName
                        },
                        combinedCancellationToken.Token
                    ).ConfigureAwait(false);

                    nextFileId = resp.NextFileID;
                    nextFileName = resp.NextFileName;

                    if (resp.Files == null || resp.Files.Length == 0)
                        break;

                    foreach (var file in resp.Files)
                    {
                        if (!file.FileName.StartsWith(_prefix, StringComparison.Ordinal))
                            continue;

                        var name = file.FileName[_prefix.Length..];
                        if (name.Contains('/'))
                            continue;

                        cache.TryGetValue(name, out var lst);
                        if (lst == null)
                            cache[name] = lst = new List<FileEntity>(1);
                        lst.Add(file);
                    }
                }
                catch (TooManyRequestException tex)
                {
                    // Backblaze's B2 API rate limit reached. Presently they don't add Retry-After headers, we will default to a delay, but respect it if present.
                    if (tex.RetryAfter == null || (tex.RetryAfter.Date == null && tex.RetryAfter.Delta == null))
                        listRetryHelper.SetRetryAfter(
                            new RetryConditionHeaderValue(TimeSpan.FromSeconds(B2_RETRY_AFTER_SECONDS)));
                    else
                        listRetryHelper.SetRetryAfter(tex.RetryAfter);

                    await listRetryHelper.WaitForRetryAfterAsync(cancellationToken);
                }
            } while (nextFileId != null);

            return prev with { FileCache = cache };
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Implementation of interface method for listing remote folder contents
    /// </summary>
    /// <returns>List of IFileEntry with directory listing result</returns>
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<FileEntity> result = null;
        await RebuildFileCache(true, cancellationToken, state => result = state.FileCache.Values.Select(x => x.OrderByDescending(y => y.UploadTimestamp).First()).ToList()).ConfigureAwait(false);
        foreach (var x in result ?? throw new Exception("File cache not initialized"))
        {
            var ts = Utility.Utility.EPOCH.AddMilliseconds(x.UploadTimestamp);
            yield return new FileEntry(x.FileName, x.Size, ts, ts);
        }
    }

    //<inheritdoc/>
    public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
    {
        await using FileStream fs = File.OpenRead(filename);
        await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Download files from remote
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="filename">Destination file to write to</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution or FileMissingException</exception>
    public async Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
    {
        await using var fs = File.Create(filename);
        await GetAsync(remotename, fs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete remote file if it exists, if now, throws FileMissingException
    /// </summary>
    /// <param name="remotename">filename to be deleted on the remote</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <returns></returns>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution</exception>
    public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutToken = new CancellationTokenSource();
            timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));
            using var combinedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancellationToken);

            var value = await SharedState.GetFileEntitiesFromCache(remotename).ConfigureAwait(false);
            if (value == null)
            {
                await RebuildFileCache(true, cancellationToken).ConfigureAwait(false);
                value = await SharedState.GetFileEntitiesFromCache(remotename).ConfigureAwait(false);

                if (value == null)
                    throw new FileMissingException();
            }

            foreach (var n in value.OrderBy(x => x.UploadTimestamp))
                await AuthHelper.PostAndGetJsonDataAsync<DeleteResponse>(
                    $"{AuthHelper.ApiUrl}/b2api/v1/b2_delete_file_version",
                    new DeleteRequest
                    {
                        FileName = _prefix + remotename,
                        FileId = n.FileID
                    },
                    combinedCancellationToken.Token
                ).ConfigureAwait(false);

            await SharedState.RentStateAsync(cache =>
            {
                if (cache.FileCache != null)
                    cache.FileCache.Remove(remotename);

                return cache;
            }).ConfigureAwait(false);
        }
        catch
        {
            await SharedState.ClearFileCache().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Performs test with backend (internally it uses the List() command, which by definition means the backend is working)
    /// </summary>
    /// <param name="cancelToken">Cancellation Token</param>
    public Task TestAsync(CancellationToken cancelToken)
        => this.TestListAsync(cancelToken);

    /// <summary>
    /// Create remote folder
    /// </summary>
    /// <param name="cancellationToken">CancellationToken that will be combined with internal timeout token</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
        using var timeoutToken = new CancellationTokenSource();
        timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));
        using var combinedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancellationToken);

        _bucket = await AuthHelper.PostAndGetJsonDataAsync<BucketEntity>(
            $"{AuthHelper.ApiUrl}/b2api/v1/b2_create_bucket",
            new BucketEntity
            {
                AccountID = AuthHelper.AccountId,
                BucketName = _bucketName,
                BucketType = _bucketType
            },
            combinedCancellationToken.Token
        ).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string DisplayName => Strings.B2.DisplayName;

    /// <inheritdoc/>
    public string ProtocolKey => "b2";

    /// <inheritdoc/>
    public string Description => Strings.B2.Description;

    /// <inheritdoc/>
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new string[] {
            new System.Uri(B2AuthHelper.AUTH_URL).Host,
            AuthHelper?.ApiDnsName,
            AuthHelper?.DownloadDnsName
        }.Where(x => !string.IsNullOrEmpty(x))
        .ToArray());

    /// <summary>
    /// Handles disposal of the backend's resources.
    /// </summary>
    public void Dispose()
    {
        lock (_sharedStateLock)
        {
            if (_ownSharedState)
            {
                _ownSharedState = false;
                _sharedState?.Dispose();
                _sharedState = null;
            }
        }
    }

    void IStateEnabledModule<B2SharedState>.SetStateModule(B2SharedState state)
    {
        _sharedState = state;
    }
}