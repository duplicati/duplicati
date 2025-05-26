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

using Duplicati.Library.Interface;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Duplicati.Library.Backend.pCloud;
using Duplicati.Library.Utility;
using Uri = System.Uri;
using System.Runtime.CompilerServices;
using Duplicati.Library.Utility.Options;

namespace Duplicati.Library.Backend;

/// <summary>
/// Native pCloud Backend API implementation
/// </summary>
public class pCloudBackend : IStreamingBackend
{
    /// <summary>
    /// The URL used to get a new token
    /// </summary>
    private static readonly string TOKEN_URL = OAuthHelperHttpClient.OAUTH_LOGIN_URL_NEW("pcloud");
    /// <summary>
    /// Implementation of interface property for the backend key
    /// </summary>
    public string ProtocolKey => "pcloud";

    /// <summary>
    /// Implementation of interface property for the backend display name
    /// </summary>
    public string DisplayName => Strings.pCloudBackend.DisplayName;

    /// <summary>
    /// Implementation of interface property for the backend description
    /// </summary>
    public string Description => Strings.pCloudBackend.Description;

    /// <summary>
    /// The server URL to be used (pcloud uses 2 different endpoints depending if its an european or non european hosting)
    /// </summary>
    private readonly string _ServerUrl;

    /// <summary>
    /// Bearer token to be using in the API
    /// </summary>
    private string _Token;

    /// <summary>
    /// Remote path/folder to use used in the backend
    /// </summary>
    private readonly string _Path;

    /// <summary>
    /// Hostname only (no ports or paths) to be used on DNS resolutions.
    /// </summary>
    private readonly string _DnsName;

    /// <summary>
    /// The timeouts to be used in the backend
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _Timeouts;

    /// <summary>
    /// HttpClient to be used for requests
    /// </summary>
    private readonly HttpClient _HttpClient;

    /// <summary>
    /// Variable being used to cache the folder ID, as it is required to upload files
    /// and the only way to obtain is with an API call. The cache is to avoid multiple
    /// requests
    /// </summary>
    private ulong? _CachedFolderID;

    /// <summary>
    /// Path separators (both Windows \ and unix /) to be used in path manipulation
    /// </summary>
    private static readonly char[] PATH_SEPARATORS = ['/', '\\'];

    /// <summary>
    /// List of pcloud Servers and their respective hostnames
    /// </summary>
    private static readonly Dictionary<string, string> PCLOUD_SERVERS = new(StringComparer.OrdinalIgnoreCase)
    {
        { "pCloud Global", "api.pcloud.com" },
        { "pCloud (EU)", "eapi.pcloud.com" },
    };

    /// <summary>
    /// Empty constructor is required for the backend to be loaded by the backend factory
    /// </summary>
    public pCloudBackend()
    {
        _ServerUrl = null!;
        _Path = null!;
        _DnsName = null!;
        _Token = null!;
        _Timeouts = null!;
        _HttpClient = null!;
    }

    /// <summary>
    /// Actual constructor for the backend that accepts the url and options
    /// </summary>
    /// <param name="url">URL in Duplicati Uri format</param>
    /// <param name="options">options to be used in the backend</param>
    public pCloudBackend(string url, Dictionary<string, string?> options)
    {
        var uri = new Utility.Uri(url);
        uri.RequireHost();
        _DnsName = uri.Host;

        _Token = AuthIdOptionsHelper.Parse(options)
            .RequireCredentials(TOKEN_URL)
            .AuthId!;

        if (!PCLOUD_SERVERS.ContainsValue(uri.Host))
            throw new UserInformationException(Strings.pCloudBackend.InvalidServerSpecified,
                "InvalidpCloudServerSpecified");

        if (string.IsNullOrWhiteSpace(uri.Host))
            throw new UserInformationException(Strings.pCloudBackend.NoServerSpecified, "NopCloudServerSpecified");

        // Ensure that the path is in the correct format, without starting or tailing slashes
        _Path = uri.Path.TrimStart(PATH_SEPARATORS).TrimEnd(PATH_SEPARATORS).Trim();
        _ServerUrl = uri.Host;
        _Timeouts = TimeoutOptionsHelper.Parse(options);

        var httpHandler = new HttpClientHandler();
        _HttpClient = HttpClientHelper.CreateClient(httpHandler);
        _HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _Token
        );

        // Set the timeout to infinite, all methods are called with cancelationTokens.
        _HttpClient.Timeout = Timeout.InfiniteTimeSpan;

    }

    /// <summary>
    /// Implementation of interface property to return supported command parameters
    /// </summary>
    public IList<ICommandLineArgument> SupportedCommands => [
        new CommandLineArgument(AuthIdOptionsHelper.AuthIdOption,
            CommandLineArgument.ArgumentType.Password,
            Strings.pCloudBackend.AuthPasswordDescriptionShort,
            Strings.pCloudBackend.AuthPasswordDescriptionLong(TOKEN_URL)),
    ];

    /// <summary>
    /// Implementation of interface method for listing remote folder contents.
    /// The root parameter is used to list the root folder, as the pCloud API
    /// when using oauth tokens creates an isolated folder Applications/ApplicationName
    /// 
    /// </summary>
    /// <param name="folderId">The folder ID to consider as root</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns></returns>
    private async Task<IEnumerable<IFileEntry>> List(ulong folderId, CancellationToken cancellationToken)
    {
        var result = await ListWithMetadata(folderId, cancellationToken).ConfigureAwait(false);
        return result.Select(item => new pCloudFileEntry
        {
            IsFolder = item.isfolder,
            Name = item.name,
            Size = item.size ?? 0,
            LastAccess = DateTime.Parse(item.created),
            LastModification = DateTime.Parse(item.modified)
        })
            .ToList();
    }

    /// <summary>
    /// Lists folders with pCloud metadata, necessary to obtain the folder IDs
    /// </summary>
    /// <param name="folderId">FolderId to be used as root.</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<List<pCloudFolderContent>> ListWithMetadata(ulong folderId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"/listfolder?folderid={folderId}", HttpMethod.Get);

        using var response = await Utility.Utility.WithTimeout(_Timeouts.ListTimeout, cancellationToken,
            ct => _HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead,
                ct)).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var content = await Utility.Utility.WithTimeout(_Timeouts.ListTimeout, cancellationToken,
            ct => response.Content.ReadAsStringAsync(ct)
        ).ConfigureAwait(false);

        var listFolderResponse = JsonSerializer.Deserialize<pCloudListFolderResponse>(content)
            ?? throw new Exception("Failed to deserialize list folder response");

        if (pCloudErrorList.ErrorMessages.TryGetValue(listFolderResponse.result, out var message))
            throw new Exception(message);

        if (listFolderResponse.result != 0)
            throw new Exception(Strings.pCloudBackend.FailedWithUnexpectedErrorCode("list", listFolderResponse.result));

        return listFolderResponse.metadata?.contents ?? [];
    }

    /// <summary>
    /// Implementation of interface method for listing remote folder contents
    /// </summary>
    /// <returns>List of IFileEntry with directory listing result</returns>
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _CachedFolderID ??= await GetFolderId(cancellationToken).ConfigureAwait(false);
        foreach (var v in await List(_CachedFolderID.Value, cancellationToken).ConfigureAwait(false))
            yield return v;
    }

    /// <summary>
    /// Upload files to remote location
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="localname">Filename to read from</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution</exception>
    public async Task PutAsync(string remotename, string localname, CancellationToken cancellationToken)
    {
        await using var fs = File.Open(localname,
            FileMode.Open, FileAccess.Read, FileShare.Read);
        await PutAsync(remotename, fs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Upload files to remote location
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="input">Stream to read from</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution</exception>
    public async Task PutAsync(string remotename, Stream input, CancellationToken cancellationToken)
    {
        _CachedFolderID ??= await GetFolderId(cancellationToken).ConfigureAwait(false);

        var encodedPath = Uri.EscapeDataString(remotename);

        using var request =
            CreateRequest($"/uploadfile?folderid={_CachedFolderID}&filename={encodedPath}&nopartial=1",
                HttpMethod.Post);
        using var timeoutStream = input.ObserveReadTimeout(_Timeouts.ReadWriteTimeout, false);
        request.Content = new StreamContent(timeoutStream);
        request.Content.Headers.ContentLength = timeoutStream.Length;
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var uploadResponse = JsonSerializer.Deserialize<pCloudUploadResponse>(content)
            ?? throw new Exception("Failed to deserialize upload response");

        if (pCloudErrorList.ErrorMessages.TryGetValue(uploadResponse.result, out var message))
            throw new Exception(message);

        if (uploadResponse.result != 0)
            throw new Exception(Strings.pCloudBackend.FailedWithUnexpectedErrorCode("upload", uploadResponse.result));
    }

    /// <summary>
    /// Download files from remote
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="output">Destination stream to write to</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution or FileMissingException</exception>
    public async Task GetAsync(string remotename, string localname, CancellationToken cancellationToken)
    {
        await using var fs = File.Open(localname,
            FileMode.Create, FileAccess.Write,
            FileShare.None);
        await GetAsync(remotename, fs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Obtains the file id from the filename to be used in the download API
    /// 
    /// </summary>
    /// <param name="filename">Filename at remote, path is automatically concatenated if needed</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns></returns>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution</exception>
    private async Task<string> GetFileLink(string filename, CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"/getfilelink?fileid={await GetFileId(filename, cancellationToken).ConfigureAwait(false)}", HttpMethod.Get);

        using var response = await Utility.Utility.WithTimeout(_Timeouts.ShortTimeout, cancellationToken,
            ct => _HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
        ).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to get download link. Status: {response.StatusCode}");

        var content = await Utility.Utility.WithTimeout(_Timeouts.ShortTimeout, cancellationToken, ct => response.Content.ReadAsStringAsync(ct)).ConfigureAwait(false);
        var getFileIdResponse = JsonSerializer.Deserialize<pCloudDownloadResponse>(content)
            ?? throw new Exception("Failed to deserialize getfilelink response");

        if (pCloudErrorList.ErrorMessages.TryGetValue(getFileIdResponse.result, out var message))
            throw new FileMissingException(message);

        if (getFileIdResponse.result != 0)
            throw new Exception(
                Strings.pCloudBackend.FailedWithUnexpectedErrorCode("getfilelink", getFileIdResponse.result));

        return $"https://{getFileIdResponse.hosts[0]}{getFileIdResponse.path}";
    }

    /// <summary>
    /// Download files from remote
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="output">Destination stream to write to</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution or FileMissingException</exception>
    public async Task GetAsync(string remotename, Stream output, CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(string.Empty, HttpMethod.Get);
            request.RequestUri = new Uri(await GetFileLink(remotename, cancellationToken).ConfigureAwait(false));

            using var timeoutStream = output.ObserveWriteTimeout(_Timeouts.ReadWriteTimeout, false);
            await _HttpClient.DownloadFile(request, timeoutStream, null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException wex)
        {
            /*
             * Known Behaviour on pCloud
             *
             * If the temporary link is no longer valid, it will return a 401
             * if the url is corrupted/wrong it will return a 500
             */

            switch (wex)
            {
                case { StatusCode: HttpStatusCode.Unauthorized }:
                    throw new FileMissingException("Temporary link is no longer valid", wex);
                case { StatusCode: HttpStatusCode.InternalServerError }:
                    throw new FileMissingException("Temporary link is corrupted", wex);
                default:
                    throw;
            }
        }
    }

    /// <summary>
    /// Delete remote file if it exists, if now, throws FileMissingException
    /// </summary>
    /// <param name="remotename">filename to be deleted on the remote</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <returns></returns>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution or business logic when return code from pcloud indicates an error.</exception>
    public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"/deletefile?fileid={await GetFileId(remotename, cancellationToken).ConfigureAwait(false)}", HttpMethod.Get);

        using var response = await Utility.Utility.WithTimeout(_Timeouts.ShortTimeout, cancellationToken,
            ct => _HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
        ).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var content = await Utility.Utility.WithTimeout(_Timeouts.ShortTimeout, cancellationToken,
            ct => response.Content.ReadAsStringAsync(ct)
        ).ConfigureAwait(false);
        var deleteFileResponse = JsonSerializer.Deserialize<pCloudDeleteResponse>(content)
            ?? throw new Exception("Failed to deserialize delete file response");

        // If no error code is matched, result was == 0 so it successfully created the folder
        if (deleteFileResponse.result == 2009)
            throw new FileMissingException();

        if (pCloudErrorList.ErrorMessages.TryGetValue(deleteFileResponse.result, out var message))
            throw new Exception(message);

        if (deleteFileResponse.result != 0)
            throw new Exception(
                Strings.pCloudBackend.FailedWithUnexpectedErrorCode("delete", deleteFileResponse.result));
    }

    /// <summary>
    /// Implementation of interface function to return hosnames used by the backend
    /// </summary>
    /// <param name="cancellationToken">CancellationToken, in this call not used.</param>
    /// <returns></returns>
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancellationToken) => Task.FromResult(new[] { _DnsName });

    /// <summary>
    /// Tests backend connectivity by verifying the configured path exists
    /// </summary>
    /// <param name="cancellationToken">The cancellation token (not used)</param>
    /// <exception cref="FolderMissingException">Thrown when configured path does not exist</exception>
    public async Task TestAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_Path) || _Path.Split(PATH_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Length == 0)
            return;

        await this.TestReadWritePermissionsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Create remote folder
    /// </summary>
    /// <param name="cancellationToken">CancellationToken that will be combined with internal timeout token</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_Path) || _Path.Split(PATH_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Length == 0)
        {
            _CachedFolderID = 0;
            return;
        }

        var currentId = 0UL;
        foreach (var folder in _Path.Split(PATH_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
            currentId = await CreateFolder(cancellationToken, currentId, folder).ConfigureAwait(false);

        _CachedFolderID = currentId;
    }

    /// <summary>
    /// Create remote folder in relation to the parent folder
    /// </summary>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <param name="parentFolderId">Parent Folder ID</param>
    /// <param name="folderName">Folder name</param>
    /// <returns>The folderID of the newly created folder</returns>
    private async Task<ulong> CreateFolder(CancellationToken cancellationToken, ulong parentFolderId, string folderName)
    {
        using var request = CreateRequest($"/createfolderifnotexists?folderid={parentFolderId}&name={folderName}", HttpMethod.Get);

        using var response = await Utility.Utility.WithTimeout(_Timeouts.ShortTimeout, cancellationToken,
            ct => _HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
        ).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var content = await Utility.Utility.WithTimeout(_Timeouts.ShortTimeout, cancellationToken,
            ct => response.Content.ReadAsStringAsync(ct)
        ).ConfigureAwait(false);
        var createFolderResponse = JsonSerializer.Deserialize<pCloudCreateFolderResponse>(content)
            ?? throw new Exception("Failed to deserialize create folder response");

        if (pCloudErrorList.ErrorMessages.TryGetValue(createFolderResponse.result, out var message))
            throw new Exception(message);

        return createFolderResponse is { result: 0, metadata.folderid: var id }
            ? id
            : throw new Exception(Strings.pCloudBackend.FailedWithUnexpectedErrorCode("createfolder", createFolderResponse.result));
    }

    /// <summary>
    /// Returns the fileID by listing the folder and searching for the filename & metadata.
    /// 
    /// For operations such as delete/getfilelink using the fileID is more reliable than using direct path/filename concatenation.
    /// </summary>
    /// <param name="name">The filename</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>The fileID</returns>
    /// <exception cref="FileMissingException"></exception>
    private async Task<ulong> GetFileId(string name, CancellationToken cancellationToken)
    {
        _CachedFolderID ??= await GetFolderId(cancellationToken).ConfigureAwait(false);

        var result = await ListWithMetadata((ulong)_CachedFolderID, cancellationToken).ConfigureAwait(false);

        return result.FirstOrDefault(x => !x.isfolder && x.name == name)?.fileid ?? throw new FileMissingException(name);

    }

    /// <summary>
    /// Returns the folder ID for the configured path, regardless of the depth
    /// </summary>
    /// <exception cref="FolderMissingException"></exception>
    private async Task<ulong> GetFolderId(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_Path) || _Path.Split(PATH_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Length == 0)
            return 0UL;

        var currentFolderId = 0UL;
        foreach (var folder in _Path.Split(PATH_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            var folderContent = await ListWithMetadata(currentFolderId, cancellationToken).ConfigureAwait(false);
            var matchingFolder = folderContent.FirstOrDefault(x => x.isfolder && x.name == folder);

            if (matchingFolder?.folderid == null)
                throw new FolderMissingException();

            currentFolderId = matchingFolder.folderid;
        }

        return currentFolderId;
    }

    /// <summary>
    /// Helper method to create request resources with the bearer token and default headers
    /// </summary>
    /// <param name="url">url to be appended after host</param>
    /// <param name="method">Http Method</param>
    /// <returns></returns>
    private HttpRequestMessage CreateRequest(string url, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://{_ServerUrl}/{url}");
        request.Headers.Add(HttpRequestHeader.UserAgent.ToString(),
            "Duplicati pCloud Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

        if (method != null)
            request.Method = method;

        return request;
    }

    /// <summary>
    /// Implementation of Dispose pattern enforced by interface
    /// in this case, we don't need to dispose anything
    /// </summary>
    public void Dispose()
    {
    }
}