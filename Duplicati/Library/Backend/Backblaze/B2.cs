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
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Duplicati.Library.Backend.Backblaze.Model;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using FileEntry = Duplicati.Library.Common.IO.FileEntry;

namespace Duplicati.Library.Backend.Backblaze;

/// <summary>
/// Lock mode options for B2 object lock
/// </summary>
public enum B2LockMode
{
    /// <summary>
    /// Governance mode - allows privileged users to bypass retention
    /// </summary>
    Governance,

    /// <summary>
    /// Compliance mode - strict retention that cannot be bypassed
    /// </summary>
    Compliance
}

/// <summary>
/// Backblaze B2 Backend
/// </summary>
public class B2 : IStreamingBackend, ILockingBackend, IRenameEnabledBackend
{
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
    /// The option key for specifying the lock mode for the backend
    /// </summary>
    private const string B2_LOCK_MODE_OPTION = "b2-lock-mode";

    /// <summary>
    /// The default bucket type for new buckets - set to private access
    /// </summary>
    private const string DEFAULT_BUCKET_TYPE = "allPrivate";

    /// <summary>
    /// The default number of files to retrieve per API request
    /// </summary>
    private const int DEFAULT_PAGE_SIZE = 500;

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
    private readonly string? _bucketType;

    /// <summary>
    /// The number of files to retrieve per API request
    /// </summary>
    private readonly int _pageSize;

    /// <summary>
    /// Custom download URL for the B2 service, if specified
    /// </summary>
    private readonly string? _downloadUrl;

    /// <summary>
    /// The lock mode to use for the backend
    /// </summary>
    private readonly B2LockMode _lockMode;

    /// <summary>
    /// Helper class for handling B2 authentication and API requests
    /// </summary>
    private readonly B2AuthHelper _b2AuthHelper;

    /// <summary>
    /// The timeout options for the backend
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _timeouts;

    /// <summary>
    /// Cached upload URL and authorization token for file uploads
    /// </summary>
    private UploadUrlResponse? _uploadUrl;

    /// <summary>
    /// Cache of file listings, mapping filenames to their versions
    /// </summary>
    private Dictionary<string, List<FileEntity>>? _filecache;

    /// <summary>
    /// The current bucket entity containing bucket information and credentials
    /// </summary>
    private BucketEntity? _bucket;

    /// <summary>
    /// A scoped instance of Http client to be used with the backend, this will use a
    /// infinite timeout as a baseline, and requests will be controlled with timeout
    /// cancellation tokens. It will be disposed when the backend is disposed
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Cache lock for BucketEntity
    /// </summary>
    private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Empty constructor is required for the backend to be loaded by the backend factory
    /// </summary>
    public B2()
    {
        _bucketName = null!;
        _prefix = null!;
        _urlencodedPrefix = null!;
        _b2AuthHelper = null!;
        _timeouts = null!;
        _httpClient = null!;
    }

    /// <summary>
    /// Actual constructor for the backend that accepts the url and options
    /// </summary>
    /// <param name="url">URL in Duplicati Uri format</param>
    /// <param name="options">options to be used in the backend</param>
    public B2(string url, Dictionary<string, string?> options)
    {
        var uri = new Utility.Uri(url);

        _bucketName = uri.Host ?? "";
        _prefix = Util.AppendDirSeparator("/" + uri.Path, "/");

        // For B2 we do not use a leading slash
        _prefix = _prefix.TrimStart('/');

        _urlencodedPrefix = string.Join("/", _prefix.Split(new[] { '/' }).Select(x => Utility.Uri.UrlPathEncode(x)));

        _bucketType = DEFAULT_BUCKET_TYPE;
        if (options.TryGetValue(B2_CREATE_BUCKET_TYPE_OPTION, out var option1))
            _bucketType = option1;

        var auth = AuthOptionsHelper.ParseWithAlias(options, uri, B2_ID_OPTION, B2_KEY_OPTION);

        if (!auth.HasUsername)
            throw new UserInformationException(Strings.B2.NoB2UserIDError, "B2MissingUserID");

        if (!auth.HasPassword)
            throw new UserInformationException(Strings.B2.NoB2KeyError, "B2MissingKey");

        _pageSize = DEFAULT_PAGE_SIZE;
        if (options.ContainsKey(B2_PAGESIZE_OPTION))
        {
            int.TryParse(options[B2_PAGESIZE_OPTION], out _pageSize);

            if (_pageSize <= 0)
                throw new UserInformationException(Strings.B2.InvalidPageSizeError(B2_PAGESIZE_OPTION, options[B2_PAGESIZE_OPTION]), "B2InvalidPageSize");
        }

        _downloadUrl = null;
        if (options.TryGetValue(B2_DOWNLOAD_URL_OPTION, out var option))
            _downloadUrl = ParseCustomDownloadUrl(option);

        // Parse lock mode option, default to Governance
        _lockMode = B2LockMode.Governance; // Default to governance
        if (options.TryGetValue(B2_LOCK_MODE_OPTION, out var lockModeOption))
        {
            if (Enum.TryParse<B2LockMode>(lockModeOption, true, out var parsedMode))
            {
                _lockMode = parsedMode;
            }
            else
            {
                throw new UserInformationException(Strings.B2.InvalidLockModeError(B2_LOCK_MODE_OPTION, lockModeOption), "B2InvalidLockMode");
            }
        }

        _httpClient = HttpClientHelper.CreateClient();
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;

        _timeouts = TimeoutOptionsHelper.Parse(options);
        _b2AuthHelper = new B2AuthHelper(auth.Username!, auth.Password!, _httpClient, _timeouts);

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
        if (_bucket != null)
            return _bucket;

        var config = await _b2AuthHelper.GetConfigAsync(cancellationToken).ConfigureAwait(false);
        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var buckets = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, async ct
                => await _b2AuthHelper.PostAndGetJsonDataAsync<ListBucketsResponse>(
                    $"{config.APIUrl}/b2api/v1/b2_list_buckets",
                    new ListBucketsRequest(accountId: config.AccountID),
                    ct).ConfigureAwait(false)
            ).ConfigureAwait(false);

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

        var config = await _b2AuthHelper.GetConfigAsync(cancellationToken).ConfigureAwait(false);
        var bucket = await GetBucketAsync(cancellationToken).ConfigureAwait(false);

        _uploadUrl = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, async ct
            => await _b2AuthHelper.PostAndGetJsonDataAsync<UploadUrlResponse>(
                $"{config.APIUrl}/b2api/v1/b2_get_upload_url",
                new UploadUrlRequest
                {
                    BucketID = bucket.BucketID
                        ?? throw new Exception("BucketID is null")
                },
                ct).ConfigureAwait(false)
        ).ConfigureAwait(false);

        return _uploadUrl;
    }

    /// <summary>
    /// Retrieves the fileID searching by filename.
    /// </summary>
    /// <param name="filename">Filename</param>
    /// <param name="cancelToken">Cancellation Token</param>
    /// <returns></returns>
    /// <exception cref="FileMissingException">Exception thrown when file is not found</exception>
    private async Task<string> GetFileId(string filename, CancellationToken cancelToken)
    {
        if (_filecache != null && _filecache.TryGetValue(filename, out var value))
            return value.OrderByDescending(x => x.UploadTimestamp).First().FileID
                ?? throw new InvalidDataException("Missing file ID");

        await RebuildFileCache(cancelToken).ConfigureAwait(false);

        if (_filecache != null && _filecache.TryGetValue(filename, out var value1))
            return value1.OrderByDescending(x => x.UploadTimestamp).First().FileID
                ?? throw new InvalidDataException("Missing file ID");

        throw new FileMissingException();
    }

    /// <summary>
    /// Normalizes and validates a user-supplied download URL override.
    /// Returns null when no override is supplied.
    /// </summary>
    /// <param name="downloadUrl">User-provided b2-download-url value</param>
    /// <returns>Normalized URL without trailing slash, or null if unset</returns>
    /// <exception cref="UserInformationException">Thrown when the URL is invalid</exception>
    private static string? ParseCustomDownloadUrl(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            return null;

        var normalized = downloadUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new UserInformationException(Strings.B2.InvalidDownloadUrlError(B2_DOWNLOAD_URL_OPTION, downloadUrl), "B2InvalidDownloadUrl");

        return normalized;
    }

    /// <summary>
    /// Resolves the download URL used for file download operations.
    /// A user-provided b2-download-url overrides the API-provided URL.
    /// </summary>
    /// <param name="defaultDownloadUrl">Download URL from B2 authorization config</param>
    /// <returns>The effective download base URL</returns>
    /// <exception cref="InvalidOperationException">Thrown when no valid URL can be resolved</exception>
    private string ResolveDownloadUrl(string? defaultDownloadUrl)
    {
        var effectiveDownloadUrl = _downloadUrl;
        if (string.IsNullOrWhiteSpace(effectiveDownloadUrl))
        {
            var normalizedDefaultUrl = defaultDownloadUrl?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalizedDefaultUrl))
                throw new InvalidOperationException("No valid Backblaze B2 download URL is available.");

            effectiveDownloadUrl = normalizedDefaultUrl;
        }

        return effectiveDownloadUrl;
    }

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands =>
        new List<ICommandLineArgument>([
            new CommandLineArgument(B2_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.B2.B2accountidDescriptionShort, Strings.B2.B2accountidDescriptionLong, null, [AuthOptionsHelper.AuthUsernameOption], null),
            new CommandLineArgument(B2_KEY_OPTION, CommandLineArgument.ArgumentType.Password, Strings.B2.B2applicationkeyDescriptionShort, Strings.B2.B2applicationkeyDescriptionLong, null, [AuthOptionsHelper.AuthPasswordOption], null),
            new CommandLineArgument(B2_CREATE_BUCKET_TYPE_OPTION, CommandLineArgument.ArgumentType.String, Strings.B2.B2createbuckettypeDescriptionShort, Strings.B2.B2createbuckettypeDescriptionLong, DEFAULT_BUCKET_TYPE),
            new CommandLineArgument(B2_PAGESIZE_OPTION, CommandLineArgument.ArgumentType.Integer, Strings.B2.B2pagesizeDescriptionShort, Strings.B2.B2pagesizeDescriptionLong, DEFAULT_PAGE_SIZE.ToString()),
            new CommandLineArgument(B2_DOWNLOAD_URL_OPTION, CommandLineArgument.ArgumentType.String, Strings.B2.B2downloadurlDescriptionShort, Strings.B2.B2downloadurlDescriptionLong),
            new CommandLineArgument(B2_LOCK_MODE_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.B2.B2lockmodeDescriptionShort, Strings.B2.B2lockmodeDescriptionLong, B2LockMode.Governance.ToString()),
            ..TimeoutOptionsHelper.GetOptions()
        ]);

    public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        (stream, var sha1, var tmp) = await Utility.Utility.CalculateThrottledStreamHash(stream, "SHA1", cancelToken).ConfigureAwait(false);
        using var _ = tmp;

        if (_filecache == null)
            await RebuildFileCache(cancelToken).ConfigureAwait(false);

        var uploadUrlData = await GetUploadUrlDataAsync(cancelToken).ConfigureAwait(false);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrlData.UploadUrl);
            using var timeoutStream = stream.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false);

            request.Headers.TryAddWithoutValidation("Authorization", uploadUrlData.AuthorizationToken);
            request.Headers.Add("X-Bz-Content-Sha1", sha1);
            request.Headers.Add("X-Bz-File-Name", _urlencodedPrefix + Utility.Uri.UrlPathEncode(remotename));
            request.Content = new StreamContent(timeoutStream);

            request.Content.Headers.Add("Content-Type", "application/octet-stream");
            request.Content.Headers.Add("Content-Length", timeoutStream.Length.ToString());

            var response = await _httpClient.UploadStream(request, cancelToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var rdata = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);

            UploadFileResponse fileinfo;
            using (var tr = new StreamReader(rdata))
            await using (var jr = new Newtonsoft.Json.JsonTextReader(tr))
                fileinfo = new Newtonsoft.Json.JsonSerializer().Deserialize<UploadFileResponse>(jr)
                    ?? throw new Exception("Failed to parse response");

            // Delete old versions
            if (_filecache!.ContainsKey(remotename))
                await DeleteAsync(remotename, cancelToken).ConfigureAwait(false);

            _filecache[remotename] =
            [
                new FileEntity
                {
                    FileID = fileinfo.FileID,
                    FileName = fileinfo.FileName,
                    Action = "upload",
                    Size = fileinfo.ContentLength,
                    UploadTimestamp = (long)(DateTime.UtcNow - Utility.Utility.EPOCH).TotalMilliseconds
                }
            ];
        }
        catch (Exception ex)
        {
            _filecache = null;

            var code = (int)B2AuthHelper.GetExceptionStatusCode(ex);
            if (code is >= 500 and <= 599)
                _uploadUrl = null;

            throw;
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
        if (_filecache == null || !_filecache.ContainsKey(remotename))
            await RebuildFileCache(cancellationToken).ConfigureAwait(false);

        var config = await _b2AuthHelper.GetConfigAsync(cancellationToken).ConfigureAwait(false);
        var downloadUrl = ResolveDownloadUrl(config.DownloadUrl);

        using var request = _filecache != null && _filecache.ContainsKey(remotename)
            ? await _b2AuthHelper.CreateRequestAsync(
                $"{downloadUrl}/b2api/v1/b2_download_file_by_id?fileId={Utility.Uri.UrlEncode(await GetFileId(remotename, cancellationToken))}", HttpMethod.Get, cancellationToken).ConfigureAwait(false)
            : await _b2AuthHelper.CreateRequestAsync(
                $"{downloadUrl}/{_urlencodedPrefix}{Utility.Uri.UrlPathEncode(remotename)}", HttpMethod.Get, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage? response = null;
        try
        {
            response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, ct => _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var timeoutStream = responseStream.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false);
            await timeoutStream.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (B2AuthHelper.GetExceptionStatusCode(ex) == HttpStatusCode.NotFound)
                throw new FileMissingException();

            await _b2AuthHelper.AttemptParseAndThrowExceptionAsync(ex, response, cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetObjectLockUntilAsync(string remotename, CancellationToken cancellationToken)
    {
        var fileId = await GetFileId(remotename, cancellationToken).ConfigureAwait(false);
        var config = await _b2AuthHelper.GetConfigAsync(cancellationToken).ConfigureAwait(false);

        var response = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, async ct
            => await _b2AuthHelper.PostAndGetJsonDataAsync<GetFileInfoResponse>(
                $"{config.APIUrl}/b2api/v2/b2_get_file_info",
                new GetFileInfoRequest
                {
                    FileId = fileId
                },
                ct
            ).ConfigureAwait(false)
        ).ConfigureAwait(false);

        if (response?.FileRetention?.Value?.RetainUntilTimestamp is long timestamp && timestamp > 0)
            return Utility.Utility.EPOCH.AddMilliseconds(timestamp);

        return null;
    }

    /// <summary>
    /// Implementation of interface method for listing remote folder contents
    /// </summary>
    /// <returns>List of IFileEntry with directory listing result</returns>
    private async Task<Dictionary<string, List<FileEntity>>> RebuildFileCache(CancellationToken cancellationToken)
    {
        _filecache = null;
        var cache = new Dictionary<string, List<FileEntity>>();
        string? nextFileId = null;
        string? nextFileName = null;
        var config = await _b2AuthHelper.GetConfigAsync(cancellationToken).ConfigureAwait(false);
        var listVersionUrl = $"{config.APIUrl}/b2api/v1/b2_list_file_versions";

        var listRetryHelper = RetryAfterHelper.CreateOrGetRetryAfterHelper(listVersionUrl);

        do
        {
            try
            {
                var resp = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, async ct
                    => await _b2AuthHelper.PostAndGetJsonDataAsync<ListFilesResponse>(
                        $"{config.APIUrl}/b2api/v1/b2_list_file_versions",
                        new ListFilesRequest
                        {
                            BucketID = (await GetBucketAsync(cancellationToken)).BucketID
                                ?? throw new Exception("BucketID is null"),
                            MaxFileCount = _pageSize,
                            Prefix = _prefix,
                            StartFileID = nextFileId,
                            StartFileName = nextFileName
                        },
                        ct
                    ).ConfigureAwait(false)
                ).ConfigureAwait(false);

                nextFileId = resp.NextFileID;
                nextFileName = resp.NextFileName;

                if (resp.Files == null || resp.Files.Length == 0)
                    break;

                foreach (var file in resp.Files)
                {
                    if (string.IsNullOrWhiteSpace(file.FileName))
                        continue;

                    if (!file.FileName.StartsWith(_prefix, StringComparison.Ordinal))
                        continue;

                    var name = file.FileName[_prefix.Length..];
                    if (name.Contains('/'))
                        continue;

                    cache.TryGetValue(name, out var lst);
                    if (lst == null)
                        cache[name] = lst = new System.Collections.Generic.List<FileEntity>(1);
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

        return _filecache = cache;
    }

    /// <summary>
    /// Implementation of interface method for listing remote folder contents
    /// </summary>
    /// <returns>List of IFileEntry with directory listing result</returns>
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var filecache = await RebuildFileCache(cancellationToken).ConfigureAwait(false);
        foreach (var x in filecache)
        {
            var newest = x.Value.OrderByDescending(y => y.UploadTimestamp).First();
            var ts = Utility.Utility.EPOCH.AddMilliseconds(newest.UploadTimestamp);
            yield return new FileEntry(x.Key, newest.Size, ts, ts);
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
            var filecache = _filecache;
            if (filecache == null || !filecache.ContainsKey(remotename))
                filecache = await RebuildFileCache(cancellationToken).ConfigureAwait(false);

            if (!filecache.TryGetValue(remotename, out var value))
                throw new FileMissingException();

            var config = await _b2AuthHelper.GetConfigAsync(cancellationToken).ConfigureAwait(false);

            foreach (var n in value.OrderBy(x => x.UploadTimestamp))
                await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, async ct
                    => await _b2AuthHelper.PostAndGetJsonDataAsync<DeleteResponse>(
                        $"{config.APIUrl}/b2api/v1/b2_delete_file_version",
                        new DeleteRequest
                        {
                            FileName = _prefix + remotename,
                            FileId = n.FileID
                        },
                        ct
                    ).ConfigureAwait(false)
                ).ConfigureAwait(false);

            filecache[remotename].Clear();
        }
        catch
        {
            _filecache = null;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetObjectLockUntilAsync(string remotename, DateTime lockUntilUtc, CancellationToken cancellationToken)
    {
        var fileId = await GetFileId(remotename, cancellationToken).ConfigureAwait(false);
        var config = await _b2AuthHelper.GetConfigAsync(cancellationToken).ConfigureAwait(false);

        var res = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, async ct
            => await _b2AuthHelper.PostAndGetJsonDataAsync<UpdateFileRetentionResponse>(
                $"{config.APIUrl}/b2api/v2/b2_update_file_retention",
                new UpdateFileRetentionRequest
                {
                    FileId = fileId,
                    FileName = _prefix + remotename,
                    FileRetention = new FileRetention
                    {
                        Mode = _lockMode.ToString().ToLower(),
                        RetainUntilTimestamp = (long)(lockUntilUtc.ToUniversalTime() - Utility.Utility.EPOCH).TotalMilliseconds
                    },
                    BypassGovernance = false
                },
                ct
            ).ConfigureAwait(false)
        ).ConfigureAwait(false);

        if (res.FileRetention?.RetainUntilTimestamp == null)
            throw new Exception("Failed to set object lock, call succeeded but no retention info returned");
    }

    /// <summary>
    /// Performs test with backend (internally it uses the List() command, which by definition means the backend is working)
    /// </summary>
    /// <param name="cancelToken">Cancellation Token</param>
    public Task TestAsync(CancellationToken cancelToken)
        => this.TestReadWritePermissionsAsync(cancelToken);

    /// <summary>
    /// Create remote folder
    /// </summary>
    /// <param name="cancellationToken">CancellationToken that will be combined with internal timeout token</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
        var config = await _b2AuthHelper.GetConfigAsync(cancellationToken);
        _bucket = await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, async ct
            => await _b2AuthHelper.PostAndGetJsonDataAsync<BucketEntity>(
                $"{config.APIUrl}/b2api/v1/b2_create_bucket",
                new BucketEntity
                {
                    AccountID = config.AccountID,
                    BucketName = _bucketName,
                    BucketType = _bucketType
                },
                ct
            ).ConfigureAwait(false)
        ).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string DisplayName => Strings.B2.DisplayName;

    /// <inheritdoc/>
    public string ProtocolKey => "b2";

    /// <inheritdoc/>
    public string Description => Strings.B2.Description;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    public async Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
    {
        var config = await _b2AuthHelper.GetConfigAsync(cancelToken);
        var downloadUrl = ResolveDownloadUrl(config.DownloadUrl);
        return new string?[] {
            B2AuthHelper.AUTH_URL,
            config.APIUrl,
            downloadUrl
        }.WhereNotNullOrWhiteSpace()
        .Select(x => new System.Uri(x).Host)
        .Distinct()
        .ToArray();
    }

    public async Task RenameAsync(string oldname, string newname, CancellationToken cancellationToken)
    {
        var sourceFileId = await GetFileId(oldname, cancellationToken).ConfigureAwait(false);
        var config = await _b2AuthHelper.GetConfigAsync(cancellationToken).ConfigureAwait(false);

        // Copy file
        await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancellationToken, async ct =>
            await _b2AuthHelper.PostAndGetJsonDataAsync<FileEntity>(
                $"{config.APIUrl}/b2api/v1/b2_copy_file",
                new CopyFileRequest(sourceFileId, _urlencodedPrefix + Utility.Uri.UrlPathEncode(newname)),
                ct).ConfigureAwait(false)
        ).ConfigureAwait(false);

        // Delete old file
        await DeleteAsync(oldname, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles disposal of the backend's resources.
    /// </summary>
    public void Dispose()
    {
        try
        {
            _httpClient?.Dispose();
        }
        catch
        {
            // ignored
        }
    }
}
