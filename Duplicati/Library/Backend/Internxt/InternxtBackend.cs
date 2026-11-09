// Copyright (C) 2026, The Duplicati Team
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
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Duplicati.Library.Backend.Internxt.Models;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Duplicati.StreamUtil;

[assembly: InternalsVisibleTo("Duplicati.UnitTest")]

namespace Duplicati.Library.Backend.Internxt;

/// <summary>
/// Backend for Internxt Drive. Content is encrypted on the client with the
/// account key, the same way the official clients do, so the files are
/// readable by the Internxt web, desktop and CLI clients.
/// </summary>
public class InternxtBackend : IStreamingBackend, IRenameEnabledBackend, IQuotaEnabledBackend
{
    private const string TwoFactorOption = "two-factor-code";
    private const string TwoFactorSecretOption = "two-factor-secret";
    private const string MoveToTrashOption = "move-to-trash";
    private const string ApiUrlOption = "api-url";
    private const string MultipartThresholdOption = "multipart-threshold";
    private const string MultipartPartSizeOption = "multipart-part-size";
    private const string ClientNameOption = "client-name";

    /// <summary>
    /// The network rejects multipart uploads below this size
    /// </summary>
    private const long MinMultipartThreshold = 100L * 1024 * 1024;
    /// <summary>
    /// S3 rejects parts below this size, except the last one
    /// </summary>
    private const long MinPartSize = 5L * 1024 * 1024;
    private const string DefaultMultipartThreshold = "100MiB";
    private const string DefaultPartSize = "30MiB";
    /// <summary>
    /// How long a freshly created folder may be invisible to other endpoints
    /// </summary>
    private static readonly TimeSpan FolderConsistencyWait = TimeSpan.FromMilliseconds(500);

    private static readonly string LOGTAG = Logging.Log.LogTagFromType<InternxtBackend>();

    private readonly AuthOptionsHelper.AuthOptions _auth;
    private readonly TimeoutOptionsHelper.Timeouts _timeout;
    private readonly string _path;
    private readonly string? _twoFactorCode;
    private readonly string? _twoFactorSecret;
    private readonly bool _moveToTrash;
    private readonly string _apiUrl;
    private readonly string _clientName;
    private readonly long _multipartThreshold;
    private readonly long _partSize;

    /// <summary>
    /// Creates the HTTP client; tests replace this with one backed by a stub handler
    /// </summary>
    internal Func<HttpClient> CreateHttpClient = () =>
    {
        var httpClient = HttpClientHelper.CreateClient();
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        return httpClient;
    };

    private InternxtClient? _client;
    private string? _folderUuid;
    private bool _fileLimitsRead;
    private long? _maxUploadSize;
    private readonly Dictionary<string, FileDto> _fileCache = new(StringComparer.Ordinal);
    private readonly object _cacheLock = new();

    /// <summary>
    /// Constructor for reflection based loading
    /// </summary>
    public InternxtBackend()
    {
        _auth = null!;
        _timeout = null!;
        _path = null!;
        _apiUrl = null!;
        _clientName = null!;
    }

    /// <summary>
    /// Creates a new instance of the backend
    /// </summary>
    /// <param name="url">The connection url</param>
    /// <param name="options">The options to use</param>
    public InternxtBackend(string url, Dictionary<string, string?> options)
    {
        var uri = new Utility.RelaxedUri(url);
        _path = Uri.UnescapeDataString(uri.HostAndPath).Trim('/');

        _auth = AuthOptionsHelper.Parse(options, uri.Username, uri.Password)
            .RequireCredentials();

        _twoFactorCode = options.GetValueOrDefault(TwoFactorOption);
        _twoFactorSecret = options.GetValueOrDefault(TwoFactorSecretOption);
        _moveToTrash = Utility.Utility.ParseBoolOption(options, MoveToTrashOption);
        _apiUrl = options.GetValueOrDefault(ApiUrlOption) is { Length: > 0 } apiUrl ? apiUrl : InternxtClient.DefaultApiUrl;
        _clientName = options.GetValueOrDefault(ClientNameOption) is { Length: > 0 } clientName ? clientName : InternxtClient.DefaultClientName;
        _multipartThreshold = Math.Max(MinMultipartThreshold, Utility.Utility.ParseSizeOption(options, MultipartThresholdOption, "mb", DefaultMultipartThreshold));
        _partSize = Math.Max(MinPartSize, Utility.Utility.ParseSizeOption(options, MultipartPartSizeOption, "mb", DefaultPartSize));
        _timeout = TimeoutOptionsHelper.Parse(options);
    }

    /// <inheritdoc/>
    public string DisplayName => Strings.InternxtBackend.DisplayName;
    /// <inheritdoc/>
    public string ProtocolKey => "internxt";
    /// <inheritdoc/>
    public string Description => Strings.InternxtBackend.Description;
    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands => [
        .. AuthOptionsHelper.GetOptions(),
        new CommandLineArgument(TwoFactorOption, CommandLineArgument.ArgumentType.String, Strings.InternxtBackend.TwoFactorShort, Strings.InternxtBackend.TwoFactorLong),
        new CommandLineArgument(TwoFactorSecretOption, CommandLineArgument.ArgumentType.Password, Strings.InternxtBackend.TwoFactorSecretShort, Strings.InternxtBackend.TwoFactorSecretLong),
        new CommandLineArgument(MoveToTrashOption, CommandLineArgument.ArgumentType.Boolean, Strings.InternxtBackend.MoveToTrashShort, Strings.InternxtBackend.MoveToTrashLong, "false"),
        new CommandLineArgument(ApiUrlOption, CommandLineArgument.ArgumentType.String, Strings.InternxtBackend.ApiUrlShort, Strings.InternxtBackend.ApiUrlLong(InternxtClient.DefaultApiUrl), InternxtClient.DefaultApiUrl),
        new CommandLineArgument(MultipartThresholdOption, CommandLineArgument.ArgumentType.Size, Strings.InternxtBackend.MultipartThresholdShort, Strings.InternxtBackend.MultipartThresholdLong, DefaultMultipartThreshold),
        new CommandLineArgument(MultipartPartSizeOption, CommandLineArgument.ArgumentType.Size, Strings.InternxtBackend.MultipartPartSizeShort, Strings.InternxtBackend.MultipartPartSizeLong, DefaultPartSize),
        new CommandLineArgument(ClientNameOption, CommandLineArgument.ArgumentType.String, Strings.InternxtBackend.ClientNameShort, Strings.InternxtBackend.ClientNameLong, InternxtClient.DefaultClientName),
        .. TimeoutOptionsHelper.GetOptions()
    ];

    /// <summary>
    /// Gets a logged-in client, refreshing or re-creating it when the token has expired
    /// </summary>
    private async Task<InternxtClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client != null && DateTime.Now > _client.ValidUntil)
        {
            try
            {
                await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct => _client.RefreshTokenAsync(ct)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "TokenRefreshFailed", ex, "Refreshing the Internxt token failed, logging in again");
                _client.Dispose();
                _client = null;
            }
        }

        if (_client == null)
        {
            var httpClient = CreateHttpClient();
            try
            {
                _client = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct =>
                    InternxtClient.CreateAsync(httpClient, _apiUrl, _auth.Username!, _auth.Password!, _twoFactorCode, _twoFactorSecret, _clientName, ct)).ConfigureAwait(false);
            }
            catch
            {
                httpClient.Dispose();
                throw;
            }
        }

        return _client;
    }

    /// <summary>
    /// Resolves the UUID of the configured folder
    /// </summary>
    private async Task<string> GetFolderUuidAsync(InternxtClient client, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_folderUuid))
            return _folderUuid;

        if (string.IsNullOrEmpty(_path))
            return _folderUuid = client.RootFolderId;

        var folder = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct => client.GetFolderByPathAsync(_path, ct)).ConfigureAwait(false);
        if (folder != null)
            return _folderUuid = folder.Uuid;

        // The path lookup may lag or be unavailable; walk the tree
        var uuid = client.RootFolderId;
        foreach (var segment in _path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var child = await Utility.Utility.WithTimeout(_timeout.ListTimeout, cancellationToken, ct =>
                client.ListFoldersAsync(uuid, ct).FirstOrDefaultAsync(f => (f.PlainName ?? f.Name) == segment, ct).AsTask()).ConfigureAwait(false);
            if (child == null)
                throw new FolderMissingException(Strings.InternxtBackend.FolderNotFound(_path));
            uuid = child.Uuid;
        }

        return _folderUuid = uuid;
    }

    /// <summary>
    /// Finds a file entry, from the cache or by asking the server
    /// </summary>
    private async Task<FileDto?> FindEntryAsync(InternxtClient client, string folderUuid, string name, CancellationToken cancellationToken)
    {
        lock (_cacheLock)
            if (_fileCache.TryGetValue(name, out var cached))
                return cached;

        var entry = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct => client.FindFileAsync(folderUuid, name, ct)).ConfigureAwait(false);
        if (entry != null)
            lock (_cacheLock)
                _fileCache[name] = entry;

        return entry;
    }

    /// <summary>
    /// Updates the cache after a confirmed server change
    /// </summary>
    private void CacheSet(string name, FileDto? entry)
    {
        lock (_cacheLock)
        {
            if (entry == null)
                _fileCache.Remove(name);
            else
                _fileCache[name] = entry;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        var folderUuid = await GetFolderUuidAsync(client, cancellationToken).ConfigureAwait(false);

        var folders = await Utility.Utility.WithTimeout(_timeout.ListTimeout, cancellationToken, ct => client.ListFoldersAsync(folderUuid, ct).ToListAsync(ct).AsTask()).ConfigureAwait(false);
        foreach (var folder in folders)
        {
            var modified = folder.ModificationTime ?? folder.UpdatedAt ?? folder.CreatedAt ?? new DateTime(0);
            yield return new FileEntry(folder.PlainName ?? folder.Name ?? string.Empty, -1, modified, modified) { IsFolder = true };
        }

        var files = await Utility.Utility.WithTimeout(_timeout.ListTimeout, cancellationToken, ct => client.ListFilesAsync(folderUuid, ct).ToListAsync(ct).AsTask()).ConfigureAwait(false);
        lock (_cacheLock)
        {
            _fileCache.Clear();
            foreach (var file in files)
                _fileCache[file.FullName] = file;
        }

        foreach (var file in files)
        {
            var modified = file.ModificationTime ?? file.UpdatedAt ?? file.CreatedAt ?? new DateTime(0);
            yield return new FileEntry(file.FullName, file.Size, new DateTime(0), modified);
        }
    }

    /// <inheritdoc/>
    public async Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filename);
        await PutAsync(remotename, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
    {
        using var stream = File.Create(filename);
        await GetAsync(remotename, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var folderUuid = await GetFolderUuidAsync(client, cancelToken).ConfigureAwait(false);
        var size = stream.Length;

        var limit = await GetMaxUploadSizeAsync(client, cancelToken).ConfigureAwait(false);
        if (limit is > 0 && size > limit)
            throw new UserInformationException(Strings.InternxtBackend.FileTooLarge(remotename, size, limit.Value), "InternxtFileTooLarge");

        string? fileId = null;
        if (size > 0)
        {
            using var timeoutStream = stream.ObserveReadTimeout(_timeout.ReadWriteTimeout, false, TimeoutMode.Gap);
            fileId = size >= _multipartThreshold
                ? await UploadMultipartAsync(client, timeoutStream, size, cancelToken).ConfigureAwait(false)
                : await UploadSinglePartAsync(client, timeoutStream, size, cancelToken).ConfigureAwait(false);
        }

        try
        {
            var existing = await FindEntryAsync(client, folderUuid, remotename, cancelToken).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            FileDto result;
            if (existing != null)
            {
                result = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancelToken, ct =>
                    client.ReplaceFileAsync(existing.Uuid, new ReplaceFileRequest { FileId = fileId, Size = size, ModificationTime = now }, ct)).ConfigureAwait(false);
            }
            else
            {
                var (plainName, type) = InternxtClient.SplitName(remotename);
                var request = new CreateFileRequest
                {
                    Bucket = client.Bucket,
                    FileId = fileId,
                    FolderUuid = folderUuid,
                    Size = size,
                    PlainName = plainName,
                    Type = type,
                    ModificationTime = now,
                    CreationTime = now,
                    Date = now
                };

                try
                {
                    result = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancelToken, ct => client.CreateFileEntryAsync(request, ct)).ConfigureAwait(false);
                }
                catch (InternxtApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    // Created by someone else since we looked; replace its content instead
                    CacheSet(remotename, null);
                    existing = await FindEntryAsync(client, folderUuid, remotename, cancelToken).ConfigureAwait(false);
                    if (existing == null)
                        throw;
                    result = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancelToken, ct =>
                        client.ReplaceFileAsync(existing.Uuid, new ReplaceFileRequest { FileId = fileId, Size = size, ModificationTime = now }, ct)).ConfigureAwait(false);
                }
            }

            // The server echoes the entry; make sure the cached copy has what later calls need
            if (string.IsNullOrEmpty(result.Uuid))
                result.Uuid = existing?.Uuid ?? string.Empty;
            result.FileId ??= fileId;
            if (result.Size == 0 && size > 0)
                result.Size = size;
            if (string.IsNullOrEmpty(result.PlainName) && string.IsNullOrEmpty(result.Name))
                (result.PlainName, result.Type) = InternxtClient.SplitName(remotename);

            CacheSet(remotename, result);
        }
        catch (Exception ex) when (fileId != null && ex is not OperationCanceledException)
        {
            // The bytes are stored but no entry points at them; try not to leave them behind
            try
            {
                await Utility.Utility.WithTimeout(_timeout.ShortTimeout, CancellationToken.None, ct => client.DeleteNetworkFileAsync(fileId, ct)).ConfigureAwait(false);
            }
            catch (Exception cleanupEx)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "OrphanCleanupFailed", cleanupEx, "Failed to remove the uploaded data for {0} after the metadata call failed", remotename);
            }
            throw;
        }
    }

    /// <summary>
    /// Reads the account's per-file upload limit once
    /// </summary>
    private async Task<long?> GetMaxUploadSizeAsync(InternxtClient client, CancellationToken cancellationToken)
    {
        if (_fileLimitsRead)
            return _maxUploadSize;

        try
        {
            var limits = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct => client.GetFileLimitsAsync(ct)).ConfigureAwait(false);
            _maxUploadSize = limits.MaxUploadFileSize;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logging.Log.WriteVerboseMessage(LOGTAG, "FileLimitsUnavailable", ex, "Could not read the Internxt upload limits, uploading without a check");
            _maxUploadSize = null;
        }

        _fileLimitsRead = true;
        return _maxUploadSize;
    }

    /// <summary>
    /// Encrypts and uploads a stream as a single network object
    /// </summary>
    /// <returns>The network file id</returns>
    private async Task<string> UploadSinglePartAsync(InternxtClient client, Stream source, long size, CancellationToken cancellationToken)
    {
        var index = RandomNumberGenerator.GetBytes(InternxtCrypto.IndexSize);
        var (key, iv) = InternxtCrypto.DeriveFileKey(client.Seed, client.Bucket, index);

        var target = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct => client.StartUploadAsync(size, 1, ct)).ConfigureAwait(false);

        using var encrypted = new AesCtrStream(source, key, iv, true);
        using var hashing = new Sha256ReadStream(encrypted, true);
        await client.UploadToUrlAsync(target.Url!, hashing, size, cancellationToken).ConfigureAwait(false);

        var shard = new FinishShard { Hash = InternxtCrypto.ShardHash(hashing.GetHash()), Uuid = target.Uuid };
        return await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct =>
            client.FinishUploadAsync(Convert.ToHexString(index).ToLowerInvariant(), shard, ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Encrypts and uploads a stream in parts
    /// </summary>
    /// <returns>The network file id</returns>
    private async Task<string> UploadMultipartAsync(InternxtClient client, Stream source, long size, CancellationToken cancellationToken)
    {
        var index = RandomNumberGenerator.GetBytes(InternxtCrypto.IndexSize);
        var (key, iv) = InternxtCrypto.DeriveFileKey(client.Seed, client.Bucket, index);
        var partCount = (int)((size + _partSize - 1) / _partSize);

        var target = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct => client.StartUploadAsync(size, partCount, ct)).ConfigureAwait(false);

        using var encrypted = new AesCtrStream(source, key, iv, true);
        using var hashing = new Sha256ReadStream(encrypted, true);

        var buffer = new byte[(int)Math.Min(_partSize, size)];
        var parts = new List<CompletedPart>(partCount);
        for (var partNumber = 1; partNumber <= partCount; partNumber++)
        {
            var remaining = size - (long)(partNumber - 1) * _partSize;
            var partLength = (int)Math.Min(_partSize, remaining);
            var read = 0;
            while (read < partLength)
            {
                var n = await hashing.ReadAsync(buffer.AsMemory(read, partLength - read), cancellationToken).ConfigureAwait(false);
                if (n == 0)
                    throw new IOException($"Stream ended after {(long)(partNumber - 1) * _partSize + read} bytes, expected {size}");
                read += n;
            }

            using var partStream = new MemoryStream(buffer, 0, partLength, false);
            using var timeoutStream = partStream.ObserveReadTimeout(_timeout.ReadWriteTimeout, false, TimeoutMode.Gap);
            var etag = await client.UploadToUrlAsync(target.Urls![partNumber - 1], timeoutStream, partLength, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(etag))
                throw new InternxtApiException(HttpStatusCode.OK, $"No ETag returned for part {partNumber}");

            parts.Add(new CompletedPart { PartNumber = partNumber, ETag = etag });
        }

        var shard = new FinishShard
        {
            Hash = InternxtCrypto.ShardHash(hashing.GetHash()),
            Uuid = target.Uuid,
            UploadId = target.UploadId,
            Parts = parts
        };

        return await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct =>
            client.FinishUploadAsync(Convert.ToHexString(index).ToLowerInvariant(), shard, ct)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var folderUuid = await GetFolderUuidAsync(client, cancelToken).ConfigureAwait(false);
        var entry = await FindEntryAsync(client, folderUuid, remotename, cancelToken).ConfigureAwait(false)
            ?? throw new FileMissingException($"File '{remotename}' not found.");

        if (string.IsNullOrEmpty(entry.FileId) || entry.Size == 0)
            return;

        FileInfoResponse info;
        try
        {
            info = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancelToken, ct => client.GetFileInfoAsync(entry.FileId, ct)).ConfigureAwait(false);
        }
        catch (InternxtApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            CacheSet(remotename, null);
            throw new FileMissingException($"File '{remotename}' not found.", ex);
        }

        if (info.Version != 2 || info.Shards.Count != 1 || string.IsNullOrEmpty(info.Index))
            throw new UserInformationException(Strings.InternxtBackend.UnsupportedFileVersion(remotename), "InternxtUnsupportedFileVersion");

        var shard = info.Shards[0];
        var (key, iv) = InternxtCrypto.DeriveFileKey(client.Seed, client.Bucket, Convert.FromHexString(info.Index));

        using var raw = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancelToken, ct => client.OpenDownloadAsync(shard.Url, ct)).ConfigureAwait(false);
        using var hashing = new Sha256ReadStream(raw, true);
        using var decrypted = new AesCtrStream(hashing, key, iv, true);
        using var timeoutStream = stream.ObserveWriteTimeout(_timeout.ReadWriteTimeout, false, TimeoutMode.Gap);
        await decrypted.CopyToAsync(timeoutStream, cancelToken).ConfigureAwait(false);

        if (!string.Equals(InternxtCrypto.ShardHash(hashing.GetHash()), shard.Hash, StringComparison.OrdinalIgnoreCase))
            throw new UserInformationException(Strings.InternxtBackend.ShardHashMismatch(remotename), "InternxtShardHashMismatch");
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        var folderUuid = await GetFolderUuidAsync(client, cancellationToken).ConfigureAwait(false);
        var entry = await FindEntryAsync(client, folderUuid, remotename, cancellationToken).ConfigureAwait(false)
            ?? throw new FileMissingException($"File '{remotename}' not found.");

        try
        {
            await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct =>
                _moveToTrash ? client.TrashFileAsync(entry.Uuid, ct) : client.DeleteFileAsync(entry.Uuid, ct)).ConfigureAwait(false);
        }
        catch (InternxtApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone; the outcome is what was asked for
            Logging.Log.WriteVerboseMessage(LOGTAG, "DeleteNotFound", "The entry for {0} was already gone when deleting", remotename);
        }

        CacheSet(remotename, null);
    }

    /// <inheritdoc/>
    public async Task RenameAsync(string oldname, string newname, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        var folderUuid = await GetFolderUuidAsync(client, cancellationToken).ConfigureAwait(false);
        var entry = await FindEntryAsync(client, folderUuid, oldname, cancellationToken).ConfigureAwait(false)
            ?? throw new FileMissingException($"File '{oldname}' not found.");

        await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct => client.RenameFileAsync(entry.Uuid, newname, ct)).ConfigureAwait(false);

        (entry.PlainName, entry.Type) = InternxtClient.SplitName(newname);
        entry.Name = null;
        CacheSet(oldname, null);
        CacheSet(newname, entry);
    }

    /// <inheritdoc/>
    public async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        var uuid = client.RootFolderId;
        foreach (var segment in _path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var child = await Utility.Utility.WithTimeout(_timeout.ListTimeout, cancellationToken, ct =>
                client.ListFoldersAsync(uuid, ct).FirstOrDefaultAsync(f => (f.PlainName ?? f.Name) == segment, ct).AsTask()).ConfigureAwait(false);
            if (child != null)
            {
                uuid = child.Uuid;
                continue;
            }

            var created = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct => client.CreateFolderAsync(uuid, segment, ct)).ConfigureAwait(false);
            if (created == null)
            {
                // Someone created it between our listing and our request
                child = await Utility.Utility.WithTimeout(_timeout.ListTimeout, cancellationToken, ct =>
                    client.ListFoldersAsync(uuid, ct).FirstOrDefaultAsync(f => (f.PlainName ?? f.Name) == segment, ct).AsTask()).ConfigureAwait(false)
                    ?? throw new FolderMissingException(Strings.InternxtBackend.FolderNotFound(_path));
                uuid = child.Uuid;
                continue;
            }

            uuid = created.Uuid;
            // New folders take a moment to show up on the other endpoints
            await Task.Delay(FolderConsistencyWait, cancellationToken).ConfigureAwait(false);
        }

        _folderUuid = uuid;
    }

    /// <inheritdoc/>
    public async Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken)
    {
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var limit = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancelToken, ct => client.GetLimitAsync(ct)).ConfigureAwait(false);
        var usage = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancelToken, ct => client.GetUsageAsync(ct)).ConfigureAwait(false);
        return new QuotaInfo(limit.MaxSpaceBytes, Math.Max(0, limit.MaxSpaceBytes - usage.Drive));
    }

    /// <inheritdoc/>
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        => Task.FromResult(new[] { new Uri(_apiUrl).Host });

    /// <inheritdoc/>
    public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken)
        => this.TestBackendAsync(alsoWrite, cancellationToken);

    /// <inheritdoc/>
    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
