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
using Duplicati.Library.Utility.Options;

namespace Duplicati.Library.Backend.Filen;

/// <summary>
/// The Filen backend
/// </summary>
public class FilenBackend : IStreamingBackend
{
    /// <summary>
    /// The two-factor option name
    /// </summary>
    private const string TwoFactorOption = "two-factor-code";
    /// <summary>
    /// The move to trash option name
    /// </summary>
    private const string MoveToTrashOption = "move-to-trash";
    /// <summary>
    /// The Filen client instance
    /// </summary>
    private FilenClient? _client;
    /// <summary>
    /// The authentication options
    /// </summary>
    private readonly AuthOptionsHelper.AuthOptions _auth;
    /// <summary>
    /// The timeout options
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _timeout;
    /// <summary>
    /// The path to the folder
    /// </summary>
    private readonly string _path;
    /// <summary>
    /// The two-factor code, if any
    /// </summary>
    private readonly string? _twoFactorCode;
    /// <summary>
    /// Whether to move files to the trash instead of deleting them
    /// </summary>
    private readonly bool _moveToTrash;
    /// <summary>
    /// The UUID of the folder files are stored in
    /// </summary>
    private string? _folderUuid;

    /// <summary>
    /// Constructor for reflection based loading
    /// </summary>
    public FilenBackend()
    {
        _client = null!;
        _auth = null!;
        _timeout = null!;
        _path = null!;
    }

    /// <summary>
    /// Creates a new instance of the Filen backend
    /// </summary>
    /// <param name="url">The connection url</param>
    /// <param name="options">The options to use</param>
    public FilenBackend(string url, Dictionary<string, string?> options)
    {
        var uri = new Utility.Uri(url);
        _path = uri.HostAndPath;

        _auth = AuthOptionsHelper.Parse(options, uri)
            .RequireCredentials();

        _moveToTrash = Utility.Utility.ParseBoolOption(options, MoveToTrashOption);
        _twoFactorCode = options.GetValueOrDefault(TwoFactorOption);
        _timeout = TimeoutOptionsHelper.Parse(options);
    }

    /// <summary>
    /// Gets a client
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to use</param>
    /// <returns>A client instance</returns>
    private async Task<FilenClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client == null || DateTime.Now > _client.ValidUntil)
        {
            _client?.Dispose();
            _client = null;
            _client = await FilenClient.CreateClientAsync(HttpClientHelper.CreateClient(), _auth.Username!, _auth.Password!, _twoFactorCode, cancellationToken).ConfigureAwait(false);
        }

        return _client;
    }

    /// <inheritdoc/>
    public string DisplayName => Strings.FilenBackend.DisplayName;
    /// <inheritdoc/>
    public string ProtocolKey => "filen";
    /// <inheritdoc/>
    public string Description => Strings.FilenBackend.Description;

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands => [
        .. AuthOptionsHelper.GetOptions(),
        new CommandLineArgument(TwoFactorOption, CommandLineArgument.ArgumentType.String, Strings.FilenBackend.TwoFactorShort, Strings.FilenBackend.TwoFactorLong),
        new CommandLineArgument(MoveToTrashOption, CommandLineArgument.ArgumentType.Boolean, Strings.FilenBackend.MoveToTrashShort, Strings.FilenBackend.MoveToTrashLong),
        .. TimeoutOptionsHelper.GetOptions()
    ];

    /// <summary>
    /// Gets the folder uuid for the folder this backend is working in
    /// </summary>
    /// <param name="client">The client to use</param>
    /// <param name="cancellationToken">The cancellation token to use</param>
    /// <returns>The folder UUID</returns>
    private async Task<string> GetFolderUuid(FilenClient client, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_folderUuid))
            return _folderUuid = await client.ResolveFolderPathAsync(_path, _timeout.ListTimeout, cancellationToken).ConfigureAwait(false);

        return _folderUuid;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IFileEntry> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        var folderId = await GetFolderUuid(client, cancellationToken).ConfigureAwait(false);

        await foreach (var entry in client.ListFolderDecryptedAsync(folderId, _timeout.ListTimeout, cancellationToken).ConfigureAwait(false))
        {
            yield return new FileEntry(entry.Name, entry.Size, new DateTime(0), entry.LastModified)
            {
                IsFolder = entry.IsFolder
            };
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
        var folderId = await GetFolderUuid(client, cancelToken).ConfigureAwait(false);
        using var timeoutStream = stream.ObserveReadTimeout(_timeout.ReadWriteTimeout, false);
        await client.UploadStreamedEncryptedFileAsync(timeoutStream, remotename, folderId, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        var client = await GetClientAsync(cancelToken).ConfigureAwait(false);
        var folderId = await GetFolderUuid(client, cancelToken).ConfigureAwait(false);
        var file = await client.GetFileEntryAsync(folderId, remotename, _timeout.ListTimeout, cancelToken).ConfigureAwait(false);

        if (file == null)
            throw new FileMissingException($"File '{remotename}' not found.");

        using var timeoutStream = stream.ObserveWriteTimeout(_timeout.ReadWriteTimeout, false);
        await client.DownloadAndDecryptToStreamAsync(file, timeoutStream, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        var folderId = await GetFolderUuid(client, cancellationToken).ConfigureAwait(false);
        var file = await client.GetFileEntryAsync(folderId, remotename, _timeout.ListTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (file == null)
            throw new FileMissingException($"File '{remotename}' not found.");

        await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct => client.DeleteFileAsync(file.Uuid, !_moveToTrash, ct)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        => Task.FromResult(
            FilenClient.GatewayUrls
            .Concat(FilenClient.EgestUrls)
            .Concat(FilenClient.IngestURLs)
            .Select(u => new System.Uri(u).Host)
            .Distinct()
            .ToArray()
    );

    /// <inheritdoc/>
    public Task TestAsync(CancellationToken cancellationToken)
        => this.TestReadWritePermissionsAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        var rootFolderId = await client.GetUserBaseFolder(cancellationToken).ConfigureAwait(false);
        foreach (var s in _path.Split('/'))
        {
            if (string.IsNullOrWhiteSpace(s))
                continue;

            var entry = await client.ListFolderDecryptedAsync(rootFolderId, _timeout.ListTimeout, cancellationToken).FirstOrDefaultAsync(f => f.Name == s).ConfigureAwait(false);
            if (entry != null)
            {
                rootFolderId = entry.Uuid;
                continue;
            }

            rootFolderId = await Utility.Utility.WithTimeout(_timeout.ShortTimeout, cancellationToken, ct => client.CreateFolderAsync(rootFolderId, s, ct)).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client?.Dispose();
    }
}
