// Copyright (C) 2024, The Duplicati Team
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
using Duplicati.Library.Interface;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.pCloud;
using Duplicati.Library.Utility;
using Uri = System.Uri;

namespace Duplicati.Library.Backend;

/// <summary>
/// Native pCloud Backend API implementation
/// </summary>
public class pCloudBackend : IStreamingBackend
{
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
    /// The default timeout in seconds for PUT/GET file operations
    /// </summary>
    private const int LONG_OPERATION_TIMEOUT_SECONDS = 30000;

    /// <summary>
    /// The default timeout in seconds for LIST/CreateFolder operations
    /// </summary>
    private const int SHORT_OPERATION_TIMEOUT_SECONDS = 30;

    /// <summary>
    /// The server URL to be used (pcloud uses 2 different endpoints depending if its an european or non european hosting)
    /// </summary>
    private string _ServerUrl;

    /// <summary>
    /// Bearer token to be using in the API
    /// </summary>
    private string _Token;

    /// <summary>
    /// Remote path/folder to use used in the backend
    /// </summary>
    private string _Path;

    /// <summary>
    /// Hostname only (no ports or paths) to be used on DNS resolutions.
    /// </summary>
    private string _DnsName;

    /// <summary>
    /// Variable being used to cache the folder ID, as it is required to upload files
    /// and the only way to obtain is with an API call. The cache is to avoid multiple
    /// requests
    /// </summary>
    private ulong? _CachedFolderID;

    private const string AUTHENTICATION_OPTION = "authid";

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
    }

    /// <summary>
    /// Actual constructor for the backend that accepts the url and options
    /// </summary>
    /// <param name="url">URL in Duplicati Uri format</param>
    /// <param name="options">options to be used in the backend</param>
    public pCloudBackend(string url, Dictionary<string, string> options)
    {
        var uri = new Utility.Uri(url);
        uri.RequireHost();
        _DnsName = uri.Host;

        if (options.TryGetValue(AUTHENTICATION_OPTION, out var option))
            _Token = option;

        if (!PCLOUD_SERVERS.ContainsValue(uri.Host))
            throw new UserInformationException(Strings.pCloudBackend.InvalidServerSpecified,
                "InvalidpCloudServerSpecified");

        if (string.IsNullOrWhiteSpace(uri.Host))
            throw new UserInformationException(Strings.pCloudBackend.NoServerSpecified, "NopCloudServerSpecified");

        _Path = uri.Path;
        _ServerUrl = uri.Host;
    }

    /// <summary>
    /// Implementation of interface property to return supported command parameters
    /// </summary>
    public IList<ICommandLineArgument> SupportedCommands
    {
        get
        {
            return new List<ICommandLineArgument>(new ICommandLineArgument[]
            {
                new CommandLineArgument(AUTHENTICATION_OPTION,
                    CommandLineArgument.ArgumentType.Password,
                    Strings.pCloudBackend.AuthPasswordDescriptionShort,
                    Strings.pCloudBackend.AuthPasswordDescriptionLong),
            });
        }
    }

    /// <summary>
    /// Implementation of interface method for listing remote folder contents.
    /// The root parameter is used to list the root folder, as the pCloud API
    /// when using oauth tokens creates an isolated folder Applications/ApplicationName
    ///
    /// When root = true, this is the directory it will be listed.
    /// When root = false, it will list the _Path directory if it exists, which is
    /// where the Duplicati backup files are stored.
    /// </summary>
    /// <param name="root">True to list absolute root, false to list _Path directory</param>
    /// <returns></returns>
    private IEnumerable<IFileEntry> List(bool root)
    {
        if (_CachedFolderID == null && string.IsNullOrWhiteSpace(_Path) == false && root == false)
            CreateFolderAsync(CancellationToken.None).Await();

        ulong folderId = root ? 0 : _CachedFolderID ?? 0;

        var timeoutToken = new CancellationTokenSource();
        timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));

        using var requestResources = CreateRequest($"/listfolder?folderid={folderId}", HttpMethod.Get);

        using var response = requestResources.HttpClient
            .SendAsync(requestResources.RequestMessage, HttpCompletionOption.ResponseContentRead,
                timeoutToken.Token).Await();

        response.EnsureSuccessStatusCode();
        var content = response.Content.ReadAsStringAsync(timeoutToken.Token).Result;
        var listFolderResponse = JsonSerializer.Deserialize<pCloudListFolderResponse>(content);

        if (pCloudErrorList.ErrorMessages.TryGetValue(listFolderResponse.result, out var message))
            throw new Exception(message);

        if (listFolderResponse.result != 0)
            throw new Exception(Strings.pCloudBackend.FailedWithUnexpectedErrorCode("list", listFolderResponse.result));

        if (listFolderResponse?.metadata?.contents == null)
            return new List<IFileEntry>();

        return listFolderResponse.metadata.contents
            .Select<pCloudFolderContent, IFileEntry>(item => new pCloudFileEntry
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
    /// Implementation of interface method for listing remote folder contents
    /// </summary>
    /// <returns>List of IFileEntry with directory listing result</returns>
    public IEnumerable<IFileEntry> List()
    {
        return List(false);
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
        // If we don't have the folder ID cached, lets fetch it with the create folder api
        if (_CachedFolderID == null)
            await CreateFolderAsync(cancellationToken).ConfigureAwait(false);

        using var timeoutToken = new CancellationTokenSource();
        timeoutToken.CancelAfter(TimeSpan.FromSeconds(LONG_OPERATION_TIMEOUT_SECONDS));
        using var combinedTokens =
            CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancellationToken);

        var encodedPath = Uri.EscapeDataString(remotename);

        using var requestResources =
            CreateRequest($"/uploadfile?folderid={_CachedFolderID}&filename={encodedPath}&nopartial=1",
                HttpMethod.Post);
        requestResources.RequestMessage.Content = new StreamContent(input);
        requestResources.RequestMessage.Content.Headers.ContentLength = input.Length;
        requestResources.RequestMessage.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/octet-stream");

        using var response = await requestResources.HttpClient.SendAsync(
            requestResources.RequestMessage,
            HttpCompletionOption.ResponseContentRead, combinedTokens.Token).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(combinedTokens.Token).ConfigureAwait(false);
        var uploadResponse = JsonSerializer.Deserialize<pCloudUploadResponse>(content);

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
    /// <exception cref="Exception">Exceptions arising from either code execution</exception>
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
    /// <returns></returns>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution</exception>
    private async Task<string> GetFileId(string filename)
    {
        var path = string.IsNullOrWhiteSpace(_Path) ? "" : $"/{_Path}";
        var encodedPath = Uri.EscapeDataString($"{path}/{filename}");

        using var requestResources = CreateRequest($"/getfilelink?path={encodedPath}", HttpMethod.Get);

        using var response = await requestResources.HttpClient.SendAsync(
            requestResources.RequestMessage,
            HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to get download link. Status: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var getFileIdResponse = JsonSerializer.Deserialize<pCloudDownloadResponse>(content);

        if (pCloudErrorList.ErrorMessages.TryGetValue(getFileIdResponse.result, out var message))
            throw new FileMissingException(message);

        if (getFileIdResponse.result != 0)
            throw new Exception(
                Strings.pCloudBackend.FailedWithUnexpectedErrorCode("getfileid", getFileIdResponse.result));

        return $"https://{getFileIdResponse.hosts[0]}{getFileIdResponse.path}";
    }

    /// <summary>
    /// Download files from remote
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="output">Destination stream to write to</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution</exception>
    public async Task GetAsync(string remotename, Stream output, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutToken = new CancellationTokenSource();
            timeoutToken.CancelAfter(TimeSpan.FromSeconds(LONG_OPERATION_TIMEOUT_SECONDS));
            using var combinedTokens =
                CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancellationToken);

            using var requestResources = CreateRequest(string.Empty, HttpMethod.Get);

            requestResources.RequestMessage.RequestUri = new Uri(await GetFileId(remotename));

            await requestResources.HttpClient.DownloadFile(requestResources.RequestMessage, output, null,
                timeoutToken.Token).ConfigureAwait(false);
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
    public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
    {
        if (!String.IsNullOrWhiteSpace(_Path))
            remotename = $"/{_Path}/{remotename}";

        using var timeoutToken = new CancellationTokenSource();
        timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));
        using var combinedTokens =
            CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancellationToken);

        using var requestResources = CreateRequest($"/deletefile?path={remotename}", HttpMethod.Get);

        using var response = requestResources.HttpClient.SendAsync(
            requestResources.RequestMessage,
            HttpCompletionOption.ResponseContentRead, combinedTokens.Token).Await();

        response.EnsureSuccessStatusCode();

        var content = response.Content.ReadAsStringAsync(combinedTokens.Token).Await();
        var deleteFileResponse = JsonSerializer.Deserialize<pCloudDeleteResponse>(content);

        // If no error code is matched, result was == 0 so it successfully created the folder
        if (deleteFileResponse.result == 2009)
            throw new FileMissingException();

        if (pCloudErrorList.ErrorMessages.TryGetValue(deleteFileResponse.result, out var message))
            throw new Exception(message);

        if (deleteFileResponse.result != 0)
            throw new Exception(
                Strings.pCloudBackend.FailedWithUnexpectedErrorCode("delete", deleteFileResponse.result));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Implementation of interface function to return hosnames used by the backend
    /// </summary>
    /// <param name="cancellationToken">CancellationToken, in this call not used.</param>
    /// <returns></returns>
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancellationToken) => Task.FromResult(new[] { _DnsName });

    /// <summary>
    /// Performs basic backend conectivity tests, in this case, checks
    /// that the configured path, if configured, exists
    /// </summary>
    /// <param name="cancellationToken">CancellationToken, in this call not used.</param>
    /// <returns></returns>
    /// <exception cref="FolderMissingException">Thrown when configured path does not exist</exception>
    public Task TestAsync(CancellationToken cancellationToken)
    {
        // List the scoped root directory and check if _Path folder exists
        if (!List(true).Any(x => x.IsFolder && x.Name == _Path) && !string.IsNullOrWhiteSpace(_Path))
            throw new FolderMissingException();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Create remote folder
    /// </summary>
    /// <param name="cancellationToken">CancellationToken that will be combined with internal timeout token</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public Task CreateFolderAsync(CancellationToken cancellationToken)
    {
        using var timeoutToken = new CancellationTokenSource();
        timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));
        using var combinedTokens =
            CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancellationToken);

        using var requestResources = CreateRequest($"/createfolderifnotexists?folderid=0&name={_Path}", HttpMethod.Get);

        using var response = requestResources.HttpClient.SendAsync(
            requestResources.RequestMessage,
            HttpCompletionOption.ResponseContentRead, combinedTokens.Token).Await();

        response.EnsureSuccessStatusCode();

        var content = response.Content.ReadAsStringAsync(combinedTokens.Token).Await();
        var createFolderResponse = JsonSerializer.Deserialize<pCloudCreateFolderResponse>(content);

        if (pCloudErrorList.ErrorMessages.TryGetValue(createFolderResponse.result, out var message))
            throw new Exception(message);
        /*
         * If no error code is matched, result was == 0 so it successfully created the folder
         * We must cache the folderID, as that will be used to upload files, reason being that
         * for oauth clients, pCloud creates a scoped folder Applications/ApplicationName
         * and the folders created with /createfolderifnotexists api are created under that folder.
         *
         * However, when uploading files, the folderid is required (and surprisingly if not provided
         * it will create the file outside the scoped folder, at the root of the drive)
         */
        if (createFolderResponse.result == 0) _CachedFolderID = createFolderResponse.metadata?.folderid;

        return Task.CompletedTask;
    }


    /// <summary>
    /// Wrapper for the tupple of HttpClient and HttpRequestMessage used in web requests.
    /// </summary>
    /// <param name="HttpClient">The HTTPClient</param>
    /// <param name="RequestMessage">The HttpRequestMessage object</param>
    private record RequestResources(HttpClient HttpClient, HttpRequestMessage RequestMessage) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                RequestMessage?.Dispose();
            }
            catch
            {
                // ignored
            }

            try
            {
                HttpClient?.Dispose();
            }
            catch
            {
                // ignored
            }
        }
    }

    /// <summary>
    /// Helper method to create request resources with the bearer token and default headers
    /// </summary>
    /// <param name="url">url to be appended after host</param>
    /// <param name="method">Http Method</param>
    /// <returns></returns>
    private RequestResources CreateRequest(string url, HttpMethod method = null)
    {
        HttpClient httpClient;
        HttpClientHandler httpHandler = new HttpClientHandler();

        httpClient = HttpClientHelper.CreateClient(httpHandler);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _Token
        );

        // Set the timeout to infinite, all methods are called with cancelationTokens.
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        var request = new HttpRequestMessage(HttpMethod.Get, $"https://{_ServerUrl}/{url}");
        request.Headers.Add(HttpRequestHeader.UserAgent.ToString(),
            "Duplicati pCloud Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

        if (method != null)
            request.Method = method;

        return new RequestResources(httpClient, request);
    }

    /// <summary>
    /// Implementation of Dispose pattern enforced by interface
    /// in this case, we don't need to dispose anything
    /// </summary>
    public void Dispose()
    {
    }
}