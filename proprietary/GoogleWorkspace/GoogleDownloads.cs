// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Google.Apis.Download;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Reads content from Google into memory, so every entry that downloads content applies the
/// same rule: a download that did not complete is a failure, never empty content
/// </summary>
internal static class GoogleDownloads
{
    /// <summary>
    /// Downloads with a media downloader, such as <c>Files.Get</c> or <c>Files.Export</c>.
    /// The downloader reports a failure through the progress instead of throwing, so the
    /// status is checked and the failure is rethrown.
    /// </summary>
    /// <param name="download">Runs the download into the stream</param>
    /// <param name="description">What is downloaded, for the error message</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The content, positioned at the start</returns>
    public static async Task<Stream> ToMemoryAsync(Func<Stream, CancellationToken, Task<IDownloadProgress>> download, string description, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();
        var progress = await download(stream, cancellationToken).ConfigureAwait(false);
        if (progress.Status != DownloadStatus.Completed)
            throw progress.Exception ?? new IOException($"The download of {description} ended with status {progress.Status}");

        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    /// <summary>
    /// Downloads from a URL with an HTTP client, such as the client of a service whose
    /// credentials the URL needs. A response that is not successful is a failure.
    /// </summary>
    /// <param name="client">The client to download with</param>
    /// <param name="url">The URL</param>
    /// <param name="description">What is downloaded, for the error message</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The content, positioned at the start</returns>
    public static async Task<Stream> ToMemoryAsync(HttpClient client, string url, string description, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new FileNotFoundException($"Cannot download {description}: {response.StatusCode}");

        var stream = new MemoryStream();
        await response.Content.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }
}
