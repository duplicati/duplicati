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
    /// The test file name that earlier versions used for every test; a file left over
    /// with this name is removed by the next test
    /// </summary>
    public const string TEST_FILE_NAME = "duplicati-access-privileges-test.txt";

    /// <summary>
    /// The start of the test file names. Each test adds a unique part, so two tests of
    /// the same destination that run at the same time do not use the same file.
    /// </summary>
    public const string TEST_FILE_PREFIX = "duplicati-access-privileges-test-";

    /// <summary>
    /// The extension of the test file names
    /// </summary>
    public const string TEST_FILE_EXTENSION = ".txt";

    /// <summary>
    /// How old a test file from another test must be before it is treated as left over.
    /// A younger file can belong to a test that is still running.
    /// </summary>
    public static readonly TimeSpan LEFTOVER_TEST_FILE_AGE = TimeSpan.FromHours(1);

    /// <summary>
    /// The test file content used to test access permissions
    /// </summary>
    public const string TEST_FILE_CONTENT = "This file is used by Duplicati to test access permissions and can be safely deleted.";

    /// <summary>
    /// Test the backend in either read-only or read-write mode.
    /// </summary>
    /// <param name="backend">The backend to test</param>
    /// <param name="alsoWrite">If the test is also checking the destination for write permissions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An awaitable tasks</returns>
    public static Task TestBackendAsync(this IBackend backend, bool alsoWrite, CancellationToken cancellationToken)
        => alsoWrite
            ? TestReadWritePermissionsAsync(backend, cancellationToken)
            : TestReadPermissionsAsync(backend, cancellationToken);

    /// <summary>
    /// Tests a backend by invoking the List() method, and attempting to write a small file on the destination.
    /// </summary>
    /// <param name="backend">Backend to test</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An awaitable tasks</returns>
    public static async Task TestReadWritePermissionsAsync(this IBackend backend, CancellationToken cancellationToken)
    {
        var testFileName = $"{TEST_FILE_PREFIX}{Guid.NewGuid():N}{TEST_FILE_EXTENSION}";

        // Remove test files left over from earlier tests
        var connected = false;
        var leftoverName = TEST_FILE_NAME;
        try
        {
            var now = DateTime.UtcNow;
            await foreach (var entry in backend
                               .ListAsync(cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                if (!IsLeftoverTestFile(entry, now))
                    continue;

                connected = true;
                leftoverName = entry.Name;
                try
                {
                    await backend.DeleteAsync(entry.Name, cancellationToken).ConfigureAwait(false);
                }
                catch (FileMissingException)
                {
                    // A listing can report a file that is already deleted, and this
                    // step only needs the file to be gone
                    Logging.Log.WriteInformationMessage(LOGTAG, "TestFileAlreadyGone", LC.L($"The listed file {entry.Name} was already gone"));
                }
            }
        }
        catch (Exception e)
            // Don't catch FolderMissingException or FileMissingException
            // so we can pass those through to the caller
            when (e is not FolderMissingException && e is not FileMissingException)
        {
            if (connected)
                throw new TestAfterConnectException(Strings.BackendExtensions.ErrorDeleteFile(leftoverName, e.Message), "TestPreparationError", e);

            throw new UserInformationException(Strings.BackendExtensions.ErrorListContent(e.Message), "TestPreparationError", e);
        }

        // Test write permissions
        try
        {
            if (backend is IStreamingBackend streamingBackend && streamingBackend.SupportsStreaming)
            {
                using (var testStream = new MemoryStream(Encoding.UTF8.GetBytes(TEST_FILE_CONTENT)))
                    await streamingBackend.PutAsync(testFileName, testStream, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using var tempfile = new TempFile();
                File.WriteAllText(tempfile, TEST_FILE_CONTENT);
                await backend.PutAsync(testFileName, tempfile, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
            // Don't catch FolderMissingException List may return empty results
            // even if the folder is missing, and we report that exception up the chain
            when (e is not FolderMissingException)
        {
            throw new TestAfterConnectException(Strings.BackendExtensions.ErrorWriteFile(testFileName, e.Message), "TestWriteError", e);
        }

        // Test read permissions
        try
        {
            if (backend is IStreamingBackend streamingBackend && streamingBackend.SupportsStreaming)
            {
                using var testStream = new MemoryStream();
                await streamingBackend.GetAsync(testFileName, testStream, cancellationToken).ConfigureAwait(false);
                var readValue = Encoding.UTF8.GetString(testStream.ToArray());
                if (readValue != TEST_FILE_CONTENT)
                    throw new Exception("Test file corrupted.");
            }
            else
            {
                using var tempfile = new TempFile();
                await backend.GetAsync(testFileName, tempfile, cancellationToken).ConfigureAwait(false);
                var readValue = File.ReadAllText(tempfile);
                if (readValue != TEST_FILE_CONTENT)
                    throw new Exception("Test file corrupted.");
            }
        }
        catch (Exception e)
        {
            try { await backend.DeleteAsync(testFileName, cancellationToken).ConfigureAwait(false); }
            catch { } // Ignore delete errors here, report the read error instead

            throw new TestAfterConnectException(Strings.BackendExtensions.ErrorReadFile(testFileName, e.Message), "TestReadError", e);
        }

        // Cleanup
        try
        {
            await backend.DeleteAsync(testFileName, cancellationToken).ConfigureAwait(false);
        }
        catch (FileMissingException)
        {
            // A file that was just written is not always addressable right away, and
            // the file being gone is what the cleanup is for
            Logging.Log.WriteInformationMessage(LOGTAG, "TestFileAlreadyGone", LC.L($"The file {testFileName} was already gone when cleaning up"));
        }
        catch (Exception e)
        {
            throw new TestAfterConnectException(Strings.BackendExtensions.ErrorDeleteFile(testFileName, e.Message), "TestCleanupError", e);
        }
    }

    /// <summary>
    /// Checks if a listed file is a test file left over from an earlier test
    /// </summary>
    /// <param name="entry">The listed file</param>
    /// <param name="now">The current time in UTC</param>
    /// <returns><c>true</c> if the file is a left over test file</returns>
    private static bool IsLeftoverTestFile(IFileEntry entry, DateTime now)
    {
        if (entry.IsFolder)
            return false;

        if (entry.Name == TEST_FILE_NAME)
            return true;

        // A file without a known time may belong to a test that is still running
        return entry.Name.StartsWith(TEST_FILE_PREFIX, StringComparison.Ordinal)
            && entry.Name.EndsWith(TEST_FILE_EXTENSION, StringComparison.Ordinal)
            && entry.LastModification != default
            && now - entry.LastModification.ToUniversalTime() > LEFTOVER_TEST_FILE_AGE;
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
