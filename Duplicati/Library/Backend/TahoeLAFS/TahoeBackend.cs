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
using System.Reflection;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Newtonsoft.Json;
using Uri = Duplicati.Library.Utility.Uri;

namespace Duplicati.Library.Backend;

public class TahoeBackend : IStreamingBackend
{
    /// <summary>
    /// Base URL for the Tahoe-LAFS backend
    /// </summary>
    private readonly string _url;

    /// <summary>
    /// The timeout options for API requests.
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _timeouts;

    /// <summary>
    /// The options for the SSL certificate validation
    /// </summary>
    private readonly SslOptionsHelper.SslCertificateOptions _certificateOptions;

    /// <summary>
    /// Cached instance of HttpClient to be used
    /// </summary>
    private HttpClient? _httpClient;

    public TahoeBackend()
    {
        _url = null!;
        _timeouts = null!;
        _certificateOptions = null!;
    }

    public TahoeBackend(string url, Dictionary<string, string?> options)
    {
        //Validate URL
        var u = new Uri(url);
        u.RequireHost();

        if (!u.Path.StartsWith("uri/URI:DIR2:", StringComparison.Ordinal) && !u.Path.StartsWith("uri/URI%3ADIR2%3A", StringComparison.Ordinal))
            throw new UserInformationException(Strings.TahoeBackend.UnrecognizedUriError, "TahoeInvalidUri");

        _certificateOptions = SslOptionsHelper.Parse(options);

        _url = u.SetScheme(_certificateOptions.UseSSL ? "https" : "http").SetQuery(null).SetCredentials(null, null).ToString();
        _url = Util.AppendDirSeparator(_url, "/");
        _timeouts = TimeoutOptionsHelper.Parse(options);
    }

    /// <inheritdoc />
    public Task TestAsync(CancellationToken cancelToken)
        => this.TestReadWritePermissionsAsync(cancelToken);

    /// <inheritdoc />
    public async Task CreateFolderAsync(CancellationToken cancelToken)
    {
        using var resp = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
            innerCancelToken =>
            {
                using var request = CreateRequest(string.Empty, "t=mkdir", HttpMethod.Post);
                return GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseContentRead, innerCancelToken);
            }).ConfigureAwait(false);

        resp.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public string DisplayName => Strings.TahoeBackend.Displayname;

    /// <inheritdoc />
    public string ProtocolKey => "tahoe";

    /// <inheritdoc />
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
    {
        TahoeEl? data;

        try
        {
            using var resp = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
               innerCancelToken =>
               {
                   using var request = CreateRequest(string.Empty, "t=json", HttpMethod.Get);
                   return GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseContentRead, innerCancelToken);
               }).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var rs = await resp.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
            using var sr = new StreamReader(rs);
            await using var jr = new JsonTextReader(sr);
            var jsr = new JsonSerializer();
            jsr.Converters.Add(new TahoeElConverter());
            data = jsr.Deserialize<TahoeEl>(jr)
                   ?? throw new Exception("Invalid folder listing response");
        }
        catch (HttpRequestException wex)
            when (wex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            throw new FolderMissingException(Strings.TahoeBackend.MissingFolderError(_url, wex.Message), wex);
        }

        if (data is not { Node: not null } || data.Nodetype != "dirnode")
            throw new Exception("Invalid folder listing response");

        foreach (var e in data.Node.Children ?? [])
        {
            if (e.Value.Node == null)
                continue;

            var isDir = e.Value.Nodetype == "dirnode";
            var isFile = e.Value.Nodetype == "filenode";

            if (!isDir && !isFile)
                continue;

            var fe = new FileEntry(e.Key)
            {
                IsFolder = isDir
            };

            if (e.Value.Node.Metadata is { Tahoe: not null })
                fe.LastModification = Utility.Utility.EPOCH + TimeSpan.FromSeconds(e.Value.Node.Metadata.Tahoe.Linkmotime);

            if (isFile)
                fe.Size = e.Value.Node.Size;

            yield return fe;
        }
    }

    /// <inheritdoc />
    public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
    {
        await using var fs = File.OpenRead(filename);
        await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
    {
        await using var fs = File.Create(filename);
        await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
    {
        try
        {
            using (await Utility.Utility.WithTimeout(_timeouts.ShortTimeout, cancelToken,
                       innerCancelToken =>
                       {
                           using var request = CreateRequest(remotename, string.Empty, HttpMethod.Delete);
                           return GetHttpClient().SendAsync(request,
                               innerCancelToken);
                       }).ConfigureAwait(false))
            { }
        }
        catch (WebException wex)
            when (wex.Response is HttpWebResponse { StatusCode: HttpStatusCode.NotFound })
        {
            throw new FileMissingException(wex);
        }
    }

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => [
        .. SslOptionsHelper.GetSslOnlyOption(), .. TimeoutOptionsHelper.GetOptions()
    ];

    /// <inheritdoc />
    public string Description => Strings.TahoeBackend.Description;

    /// <inheritdoc />
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { new System.Uri(_url).Host });

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <inheritdoc />
    public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        try
        {
            using var request = CreateRequest(remotename, string.Empty, HttpMethod.Put);

            await using var timeoutStream = stream.ObserveReadTimeout(_timeouts.ReadWriteTimeout, false);
            request.Content = new StreamContent(timeoutStream);

            request.Content.Headers.Add("Content-Type", "application/binary");
            request.Content.Headers.Add("Content-Length", timeoutStream.Length.ToString());

            using var response = await GetHttpClient().UploadStream(request, cancelToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException wex)
            when (wex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            throw new FolderMissingException(Strings.TahoeBackend.MissingFolderError(_url, wex.Message), wex);
        }
    }

    /// <inheritdoc />
    public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
    {
        using var resp = await Utility.Utility.WithTimeout(_timeouts.ListTimeout, cancelToken,
            innerCancelToken =>
            {
                using var request = CreateRequest(remotename, string.Empty, HttpMethod.Get);
                return GetHttpClient().SendAsync(request, innerCancelToken);
            }).ConfigureAwait(false);

        resp.EnsureSuccessStatusCode();

        await using var s = await resp.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
        await using var t = s.ObserveReadTimeout(_timeouts.ReadWriteTimeout);
        await Utility.Utility.CopyStreamAsync(t, stream, true, cancelToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Prepares the base request for Tahoe-LAFS
    /// </summary>
    /// <param name="remotename">Remotename parameter</param>
    /// <param name="queryparams">Querystring parameters</param>
    /// <param name="method">Http Method</param>
    /// <returns></returns>
    private HttpRequestMessage CreateRequest(string remotename, string queryparams, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method == null ? HttpMethod.Get : method, $"{_url}{Uri.UrlEncode(remotename).Replace("+", "%20")}{(string.IsNullOrEmpty(queryparams) || queryparams.Trim().Length == 0 ? String.Empty : "?" + queryparams)}");
        request.Headers.UserAgent.ParseAdd($"Duplicati Tahoe-LAFS Client {Assembly.GetExecutingAssembly().GetName().Version?.ToString()}");
        return request;
    }

    /// <summary>
    /// Returns the HttpClient instance to use for requests, cached for reuse.
    /// </summary>
    private HttpClient GetHttpClient()
    {
        if (_httpClient != null) return _httpClient;

        _httpClient = HttpClientHelper.CreateClient(_certificateOptions.CreateHandler());
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;

        return _httpClient;
    }
}