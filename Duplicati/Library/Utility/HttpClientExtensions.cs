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

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Duplicati.Library.Utility;

/// <summary>
/// Extension methods to wrap functionality around the HttpClient class with support 
/// for cancelation via CancellationToken and progress reporting stream
/// </summary>
public static class HttpClientExtensions
{

    /// <summary>
    /// Downloads a file from the server and saves it to the specified filename
    /// </summary>
    /// <param name="client">The Http client reference</param>
    /// <param name="request">A prepared HttpRequestMessage</param>
    /// <param name="filename">Filename to created</param>
    /// <param name="progressReportingAction">Action for progress reporting</param>
    /// <param name="cancellationToken">Cancelation token</param>
    /// <returns></returns>
    public static async Task DownloadFile(this HttpClient client, HttpRequestMessage request, string filename, Action<long>? progressReportingAction = null, CancellationToken cancellationToken = default)
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = System.IO.File.Create(filename);
        if (progressReportingAction != null)
        {
            using var ProgressReportingStream = new ProgressReportingStream(stream, progressReportingAction);
            await ProgressReportingStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Downloads a file from the server and saves it to the specified filename
    /// </summary>
    /// <param name="client">The Http client reference</param>
    /// <param name="request">A prepared HttpRequestMessage</param>
    /// <param name="fileStream">Stream to write downloaded data</param>
    /// <param name="progressReportingAction">Action for progress reporting</param>
    /// <param name="cancellationToken">Cancelation token</param>
    /// <returns></returns>
    public static async Task DownloadFile(this HttpClient client, HttpRequestMessage request, Stream fileStream, Action<long>? progressReportingAction = null, CancellationToken cancellationToken = default)
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (progressReportingAction != null)
        {
            using var ProgressReportingStream = new ProgressReportingStream(stream, progressReportingAction);
            await ProgressReportingStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes an asyc request uploading the stream to the server and returns when all content has been uploaded
    /// </summary>
    /// <param name="client">The Http client reference</param>
    /// <param name="request">A prepared HttpRequestMessage (Presumably with a stream)</param>
    /// <param name="cancellationToken">Cancelation token</param>
    public static Task<HttpResponseMessage> UploadStream(this HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken = default)
        => client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
}
