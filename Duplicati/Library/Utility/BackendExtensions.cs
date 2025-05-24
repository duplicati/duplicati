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

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Utility;

/// <summary>
/// Contains extension methods for the IBackend interfaces
/// </summary>
public static class BackendExtensions
{
    /// <summary>
    /// The test file name used to test access permissions
    /// </summary>
    public const string TEST_FILE_NAME = "duplicati-access-privileges-test.tmp";

    /// <summary>
    /// The test file content used to test access permissions
    /// </summary>
    public const string TEST_FILE_CONTENT = "This file is used by Duplicati to test access permissions and can be safely deleted.";

    /// <summary>
    /// Tests a backend by invoking the List() method.
    /// As long as the iteration can either complete or find at least one file without throwing, the test is successful
    /// </summary>
    /// <param name="backend">Backend to test</param>
    /// <param name="cancelToken">Cancellation token</param>
    public static async Task TestReadWritePermissionsAsync(this IBackend backend, CancellationToken cancellationToken)
    {
        // Remove the file if it exists
        var connected = false;
        try
        {
            if (await backend.ListAsync(cancellationToken).AnyAsync(entry => entry.Name == TEST_FILE_NAME, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                connected = true;
                await backend.DeleteAsync(TEST_FILE_NAME, cancellationToken).ConfigureAwait(false);
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
            if (backend is IStreamingBackend streamingBackend)
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
        try
        {
            if (backend is IStreamingBackend streamingBackend)
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
        catch (Exception e)
        {
            throw new TestAfterConnectException(Strings.BackendExtensions.ErrorDeleteFile(TEST_FILE_NAME, e.Message), "TestCleanupError", e);
        }
    }
}
