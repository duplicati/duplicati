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

#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Utility;

/// <summary>
/// Contains extension methods for the IBackend interfaces
/// </summary>
public static class BackendExtensions
{
    /// <summary>
    /// The log tag for messages from this class
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(BackendExtensions));

    /// <summary>
    /// The test file name used to test access permissions
    /// </summary>
    public const string TEST_FILE_NAME = "duplicati-access-privileges-test.tmp";

    /// <summary>
    /// The test file content used to test access permissions
    /// </summary>
    public const string TEST_FILE_CONTENT = "This file is used by Duplicati to test access permissions and can be safely deleted.";

    /// <summary>
    /// The default delays between read attempts of the test file.
    /// Keeps the test quick for backends that are immediately consistent.
    /// </summary>
    private static readonly TimeSpan[] DefaultReadRetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)];

    /// <summary>
    /// Test the backend in either read-only or read-write mode.
    /// </summary>
    /// <param name="backend">The backend to test</param>
    /// <param name="alsoWrite">If the test is also checking the destination for write permissions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An awaitable tasks</returns>
    public static Task TestBackendAsync(this IBackend backend, bool alsoWrite, TimeSpan[] readRetryDelays, CancellationToken cancellationToken)
        => alsoWrite
            ? TestReadWritePermissionsAsync(backend, readRetryDelays, cancellationToken)
            : TestReadPermissionsAsync(backend, cancellationToken);

    /// <summary>
    /// Test the backend in either read-only or read-write mode.
    /// </summary>
    /// <param name="backend">The backend to test</param>
    /// <param name="alsoWrite">If the test is also checking the destination for write permissions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An awaitable tasks</returns>
    public static Task TestBackendAsync(this IBackend backend, bool alsoWrite, CancellationToken cancellationToken)
        => TestBackendAsync(backend, alsoWrite, DefaultReadRetryDelays, cancellationToken);

    /// <summary>
    /// Tests a backend by invoking the List() method, and attempting to write a small file on the destination.
    /// </summary>
    /// <param name="backend">Backend to test</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An awaitable tasks</returns>
    public static Task TestReadWritePermissionsAsync(this IBackend backend, CancellationToken cancellationToken)
        => TestReadWritePermissionsAsync(backend, DefaultReadRetryDelays, cancellationToken);

    /// <summary>
    /// Tests a backend by invoking the List() method, and attempting to write a small file on the destination.
    /// </summary>
    /// <param name="backend">Backend to test</param>
    /// <param name="readRetryDelays">The delays between read attempts of the test file; the number of entries bounds the number of retries</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An awaitable tasks</returns>
    internal static async Task TestReadWritePermissionsAsync(this IBackend backend, TimeSpan[] readRetryDelays, CancellationToken cancellationToken)
    {
        // Remove the file if it exists
        var connected = false;
        try
        {
            await foreach (var entry in backend
                               .ListAsync(cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                if (entry.Name != TEST_FILE_NAME)
                    continue;

                connected = true;
                try
                {
                    await backend.DeleteAsync(TEST_FILE_NAME, cancellationToken).ConfigureAwait(false);
                }
                catch (FileMissingException)
                {
                    // A listing can report a file that is already deleted, and this
                    // step only needs the file to be gone
                    Logging.Log.WriteInformationMessage(LOGTAG, "TestFileAlreadyGone", LC.L($"The listed file {TEST_FILE_NAME} was already gone"));
                }
                break;
            }
        }
        catch (Exception e)
            // Don't catch FolderMissingException or FileMissingException
            // so we can pass those through to the caller
            when (e is not FolderMissingException && e is not FileMissingException)
        {
            if (connected)
                throw new TestAfterConnectException(Strings.BackendExtensions.ErrorDeleteFile(TEST_FILE_NAME, e.Message), "TestPreparationError", e);

            throw new UserInformationException(Strings.BackendExtensions.ErrorListContent(e.Message), "TestPreparationError", e);
        }

        // Test write permissions
        try
        {
            if (backend is IStreamingBackend streamingBackend && streamingBackend.SupportsStreaming)
            {
                using (var testStream = new MemoryStream(Encoding.UTF8.GetBytes(TEST_FILE_CONTENT)))
                    await streamingBackend.PutAsync(TEST_FILE_NAME, testStream, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using var tempfile = new TempFile();
                File.WriteAllText(tempfile, TEST_FILE_CONTENT);
                await backend.PutAsync(TEST_FILE_NAME, tempfile, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
            // Don't catch FolderMissingException List may return empty results
            // even if the folder is missing, and we report that exception up the chain
            when (e is not FolderMissingException)
        {
            throw new TestAfterConnectException(Strings.BackendExtensions.ErrorWriteFile(TEST_FILE_NAME, e.Message), "TestWriteError", e);
        }

        // Test read permissions
        // A just-written file is not always immediately addressable on eventually
        // consistent backends, and a negative (404) result can be cached and served
        // for a while, so allow retrying the read before giving up.
        try
        {
            var retries = 0;
            while (true)
            {
                try
                {
                    if (backend is IStreamingBackend streamingBackend && streamingBackend.SupportsStreaming)
                    {
                        using var testStream = new MemoryStream();
                        await streamingBackend.GetAsync(TEST_FILE_NAME, testStream, cancellationToken).ConfigureAwait(false);
                        var readValue = Encoding.UTF8.GetString(testStream.ToArray());
                        if (readValue != TEST_FILE_CONTENT)
                            throw new Exception("Test file corrupted.");
                    }
                    else
                    {
                        using var tempfile = new TempFile();
                        await backend.GetAsync(TEST_FILE_NAME, tempfile, cancellationToken).ConfigureAwait(false);
                        var readValue = File.ReadAllText(tempfile);
                        if (readValue != TEST_FILE_CONTENT)
                            throw new Exception("Test file corrupted.");
                    }

                    break;
                }
                catch (FileMissingException) when (retries < readRetryDelays.Length)
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "ReadAfterUploadFailure", LC.L($"Retrying read of {TEST_FILE_NAME} after upload"));
                    await Task.Delay(readRetryDelays[retries], cancellationToken).ConfigureAwait(false);
                    retries++;
                }
            }
        }
        catch (Exception e)
        {
            try { await backend.DeleteAsync(TEST_FILE_NAME, cancellationToken).ConfigureAwait(false); }
            catch { } // Ignore delete errors here, report the read error instead

            throw new TestAfterConnectException(Strings.BackendExtensions.ErrorReadFile(TEST_FILE_NAME, e.Message), "TestReadError", e);
        }

        // Cleanup
        try
        {
            await backend.DeleteAsync(TEST_FILE_NAME, cancellationToken).ConfigureAwait(false);
        }
        catch (FileMissingException)
        {
            // A file that was just written is not always addressable right away, and
            // the file being gone is what the cleanup is for
            Logging.Log.WriteInformationMessage(LOGTAG, "TestFileAlreadyGone", LC.L($"The file {TEST_FILE_NAME} was already gone when cleaning up"));
        }
        catch (Exception e)
        {
            throw new TestAfterConnectException(Strings.BackendExtensions.ErrorDeleteFile(TEST_FILE_NAME, e.Message), "TestCleanupError", e);
        }
    }

    /// <summary>
    /// Tests a backend by invoking the List() method.
    /// As long as the iteration can return one page, the test is considered successful.
    /// </summary>
    /// <param name="backend">Backend to test</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An awaitable tasks</returns>
    public static async Task TestReadPermissionsAsync(this IBackend backend, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var entry in backend
                               .ListAsync(cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                break;
            }
        }
        catch (Exception e)
            // Don't catch FolderMissingException or FileMissingException
            // so we can pass those through to the caller
            when (e is not FolderMissingException && e is not FileMissingException)
        {
            throw new UserInformationException(Strings.BackendExtensions.ErrorListContent(e.Message), "TestPreparationError", e);
        }
    }
}
