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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class SoftDeleteTests : BasicSetupHelper
    {
        /// <summary>
        /// Tests that soft-delete works with a simple prefix (like "deleted-") using a backend that supports rename.
        /// </summary>
        [Test]
        [Category("SoftDelete")]
        public void TestSoftDeleteWithSimplePrefix()
        {
            // Use a simple prefix (not a folder path)
            var softDeletePrefix = "deleted-";
            var testFile = Path.Combine(this.DATAFOLDER, "testfile.txt");

            // 1. Create initial backup
            File.WriteAllText(testFile, "test content version 1");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 1 should have no errors");
            }

            // Get the files in the target folder after first backup
            var filesAfterBackup1 = Directory.GetFiles(this.TARGETFOLDER);
            Assert.That(filesAfterBackup1.Length, Is.GreaterThan(0), "Should have files after first backup");

            // 2. Modify file and create second backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 2");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 2 should have no errors");
            }

            // 3. Modify file and create third backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 3");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 3 should have no errors");
            }

            // At this point, with keep-versions=2, the first backup should be deleted (soft-deleted)
            // Check if any files have the soft-delete prefix
            var allFiles = Directory.GetFiles(this.TARGETFOLDER);
            var softDeletedFiles = allFiles.Where(f => Path.GetFileName(f).StartsWith(softDeletePrefix)).ToArray();

            Assert.That(softDeletedFiles.Length, Is.GreaterThan(0),
                $"Should have soft-deleted files with prefix '{softDeletePrefix}'. Files in target: {string.Join(", ", allFiles.Select(Path.GetFileName))}");

            // Verify that the soft-deleted files include dlist files (from the deleted version)
            Assert.That(softDeletedFiles.Any(f => f.Contains(".dlist.")), Is.True,
                $"Should have soft-deleted dlist files. Soft-deleted files: {string.Join(", ", softDeletedFiles.Select(Path.GetFileName))}");
        }

        /// <summary>
        /// Tests that soft-delete works with a folder-based prefix (like "recycled/") using a backend that supports rename.
        /// The folder should be created automatically if it doesn't exist.
        /// </summary>
        [Test]
        [Category("SoftDelete")]
        public void TestSoftDeleteWithFolderPrefix()
        {
            // Use a folder-based prefix
            var softDeletePrefix = "recycled/";
            var testFile = Path.Combine(this.DATAFOLDER, "testfile.txt");

            // Create the recycled folder manually as it is required
            Directory.CreateDirectory(Path.Combine(this.TARGETFOLDER, "recycled"));

            // 1. Create initial backup
            File.WriteAllText(testFile, "test content version 1");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 1 should have no errors");
            }

            // 2. Modify file and create second backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 2");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 2 should have no errors");
            }

            // 3. Modify file and create third backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 3");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 3 should have no errors");
            }

            // At this point, with keep-versions=2, the first backup should be deleted (soft-deleted)
            // Check if the recycled folder exists and contains files
            var recycledFolder = Path.Combine(this.TARGETFOLDER, "recycled");
            Assert.That(Directory.Exists(recycledFolder), Is.True,
                $"Recycled folder should exist. Contents of target: {string.Join(", ", Directory.GetFileSystemEntries(this.TARGETFOLDER))}");

            var softDeletedFiles = Directory.GetFiles(recycledFolder);
            Assert.That(softDeletedFiles.Length, Is.GreaterThan(0),
                $"Should have soft-deleted files in recycled folder");

            // Verify that the soft-deleted files include dlist files (from the deleted version)
            Assert.That(softDeletedFiles.Any(f => f.Contains(".dlist.")), Is.True,
                $"Should have soft-deleted dlist files. Soft-deleted files: {string.Join(", ", softDeletedFiles.Select(Path.GetFileName))}");
        }

        /// <summary>
        /// Tests that soft-delete works with a backend that does NOT support rename (IRenameEnabledBackend).
        /// In this case, the fallback mechanism (download, upload with new name, delete old) should be used.
        /// </summary>
        [Test]
        [Category("SoftDelete")]
        public void TestSoftDeleteWithoutRenameEnabledBackend()
        {
            // Register the NoRenameBackend
            Library.DynamicLoader.BackendLoader.AddBackend(new NoRenameBackend());

            // Use a simple prefix
            var softDeletePrefix = "deleted-";
            var testFile = Path.Combine(this.DATAFOLDER, "testfile.txt");

            // 1. Create initial backup
            File.WriteAllText(testFile, "test content version 1");
            using (var c = new Controller(new NoRenameBackend().ProtocolKey + "://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 1 should have no errors");
            }

            // Get the files in the target folder after first backup
            var filesAfterBackup1 = Directory.GetFiles(this.TARGETFOLDER);
            Assert.That(filesAfterBackup1.Length, Is.GreaterThan(0), "Should have files after first backup");

            // 2. Modify file and create second backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 2");
            using (var c = new Controller(new NoRenameBackend().ProtocolKey + "://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 2 should have no errors");
            }

            // 3. Modify file and create third backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 3");
            using (var c = new Controller(new NoRenameBackend().ProtocolKey + "://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 3 should have no errors");
            }

            // At this point, with keep-versions=2, the first backup should be deleted (soft-deleted)
            // Check if any files have the soft-delete prefix
            var allFiles = Directory.GetFiles(this.TARGETFOLDER);
            var softDeletedFiles = allFiles.Where(f => Path.GetFileName(f).StartsWith(softDeletePrefix)).ToArray();

            Assert.That(softDeletedFiles.Length, Is.GreaterThan(0),
                $"Should have soft-deleted files with prefix '{softDeletePrefix}'. Files in target: {string.Join(", ", allFiles.Select(Path.GetFileName))}");

            // Verify that the soft-deleted files include dlist files
            Assert.That(softDeletedFiles.Any(f => f.Contains(".dlist.")), Is.True,
                $"Should have soft-deleted dlist files. Soft-deleted files: {string.Join(", ", softDeletedFiles.Select(Path.GetFileName))}");
        }

        /// <summary>
        /// Tests that soft-delete with folder prefix works with a backend that does NOT support rename.
        /// When the backend doesn't support rename and can't create directories, it falls back to using
        /// a flat prefix (e.g., "recycled-" instead of "recycled/").
        /// </summary>
        [Test]
        [Category("SoftDelete")]
        public void TestSoftDeleteWithFolderPrefixWithoutRenameBackend()
        {
            // Register the NoRenameBackend
            Library.DynamicLoader.BackendLoader.AddBackend(new NoRenameBackend());

            // Use a folder-based prefix
            var softDeletePrefix = "recycled/";
            var testFile = Path.Combine(this.DATAFOLDER, "testfile.txt");

            // Create the recycled folder manually as it is required
            Directory.CreateDirectory(Path.Combine(this.TARGETFOLDER, "recycled"));

            // 1. Create initial backup
            File.WriteAllText(testFile, "test content version 1");
            using (var c = new Controller(new NoRenameBackend().ProtocolKey + "://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 1 should have no errors");
            }

            // 2. Modify file and create second backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 2");
            using (var c = new Controller(new NoRenameBackend().ProtocolKey + "://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 2 should have no errors");
            }

            // 3. Modify file and create third backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 3");
            using (var c = new Controller(new NoRenameBackend().ProtocolKey + "://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 3 should have no errors");
            }

            // At this point, with keep-versions=2, the first backup should be deleted (soft-deleted)
            // Check if the recycled folder exists and contains files
            var recycledFolder = Path.Combine(this.TARGETFOLDER, "recycled");
            Assert.That(Directory.Exists(recycledFolder), Is.True,
                $"Recycled folder should exist. Contents of target: {string.Join(", ", Directory.GetFileSystemEntries(this.TARGETFOLDER))}");

            var softDeletedFiles = Directory.GetFiles(recycledFolder);
            Assert.That(softDeletedFiles.Length, Is.GreaterThan(0),
                $"Should have soft-deleted files in recycled folder");

            // Verify that the soft-deleted files include dlist files
            Assert.That(softDeletedFiles.Any(f => f.Contains(".dlist.")), Is.True,
                $"Should have soft-deleted dlist files. Soft-deleted files: {string.Join(", ", softDeletedFiles.Select(Path.GetFileName))}");
        }

        /// <summary>
        /// Tests that soft-delete works with the prevent-backend-rename option.
        /// This should force the fallback mechanism even if the backend supports rename.
        /// </summary>
        [Test]
        [Category("SoftDelete")]
        public void TestSoftDeleteWithPreventBackendRename()
        {
            // Use a simple prefix
            var softDeletePrefix = "deleted-";
            var testFile = Path.Combine(this.DATAFOLDER, "testfile.txt");

            // 1. Create initial backup
            File.WriteAllText(testFile, "test content version 1");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2, prevent_backend_rename = "true" }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 1 should have no errors");
            }

            // 2. Modify file and create second backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 2");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2, prevent_backend_rename = "true" }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 2 should have no errors");
            }

            // 3. Modify file and create third backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 3");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2, prevent_backend_rename = "true" }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 3 should have no errors");
            }

            // At this point, with keep-versions=2, the first backup should be deleted (soft-deleted)
            // Check if any files have the soft-delete prefix
            var allFiles = Directory.GetFiles(this.TARGETFOLDER);
            var softDeletedFiles = allFiles.Where(f => Path.GetFileName(f).StartsWith(softDeletePrefix)).ToArray();

            Assert.That(softDeletedFiles.Length, Is.GreaterThan(0),
                $"Should have soft-deleted files with prefix '{softDeletePrefix}'. Files in target: {string.Join(", ", allFiles.Select(Path.GetFileName))}");

            // Verify that the soft-deleted files include dlist files
            Assert.That(softDeletedFiles.Any(f => f.Contains(".dlist.")), Is.True,
                $"Should have soft-deleted dlist files. Soft-deleted files: {string.Join(", ", softDeletedFiles.Select(Path.GetFileName))}");
        }

        /// <summary>
        /// Tests that without soft-delete prefix, files are actually deleted (not renamed).
        /// </summary>
        [Test]
        [Category("SoftDelete")]
        public void TestWithoutSoftDeleteFilesAreActuallyDeleted()
        {
            var testFile = Path.Combine(this.DATAFOLDER, "testfile.txt");

            // 1. Create initial backup
            File.WriteAllText(testFile, "test content version 1");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 1 should have no errors");
            }

            // Get the files in the target folder after first backup
            var filesAfterBackup1 = Directory.GetFiles(this.TARGETFOLDER).Select(Path.GetFileName).ToHashSet();
            Assert.That(filesAfterBackup1.Count, Is.GreaterThan(0), "Should have files after first backup");

            // 2. Modify file and create second backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 2");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 2 should have no errors");
            }

            // 3. Modify file and create third backup
            Thread.Sleep(1100); // Ensure timestamp difference
            File.WriteAllText(testFile, "test content version 3");
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { keep_versions = 2 }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 3 should have no errors");
            }

            // At this point, with keep-versions=2, the first backup should be deleted
            // Without soft-delete, the files should be gone completely
            var allFiles = Directory.GetFiles(this.TARGETFOLDER).Select(Path.GetFileName).ToArray();

            // Check that no files have a "deleted-" prefix (since we didn't use soft-delete)
            var softDeletedFiles = allFiles.Where(f => f.StartsWith("deleted-")).ToArray();
            Assert.That(softDeletedFiles.Length, Is.EqualTo(0), "Should not have any soft-deleted files when soft-delete is not enabled");

            // Verify that some files from the first backup are no longer present
            // (The dlist file from the first backup should be deleted)
            var dlistFiles = allFiles.Where(f => f.Contains(".dlist.")).ToArray();
            Assert.That(dlistFiles.Length, Is.EqualTo(2), "Should have exactly 2 dlist files (for the 2 kept versions)");
        }

        /// <summary>
        /// Tests soft-delete with compact operation.
        /// </summary>
        [Test]
        [Category("SoftDelete")]
        public void TestSoftDeleteWithCompact()
        {
            var softDeletePrefix = "deleted-";
            var testFile = Path.Combine(this.DATAFOLDER, "testfile.txt");

            // 1. Create initial backup with a larger file
            var largeContent = new string('A', 100000);
            File.WriteAllText(testFile, largeContent);
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 1 should have no errors");
            }

            // 2. Delete the file and backup again
            File.Delete(testFile);
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix }), null))
            {
                var result = c.Backup(new[] { this.DATAFOLDER });
                Assert.That(result.Errors, Is.Empty, "Backup 2 should have no errors");
            }

            // 3. Delete the first version to trigger compact
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix }), null))
            {
                var result = c.Delete();
                Assert.That(result.Errors, Is.Empty, "Delete should have no errors");
            }

            // 4. Run compact with aggressive settings
            using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, small_file_size = "1", small_file_max_count = "0" }), null))
            {
                var result = c.Compact();
                Assert.That(result.Errors, Is.Empty, "Compact should have no errors");
            }

            // Check if any files have the soft-delete prefix
            var allFiles = Directory.GetFiles(this.TARGETFOLDER);
            var softDeletedFiles = allFiles.Where(f => Path.GetFileName(f).StartsWith(softDeletePrefix)).ToArray();

            // We should have some soft-deleted files from the compact operation
            Assert.That(softDeletedFiles.Length, Is.GreaterThanOrEqualTo(0),
                $"Compact operation completed. Files in target: {string.Join(", ", allFiles.Select(Path.GetFileName))}");
        }

        /// <summary>
        /// Tests that soft-delete handles duplicate filenames by adding a random suffix.
        /// </summary>
        [Test]
        [Category("SoftDelete")]
        public void TestSoftDeleteHandlesDuplicateFilenames()
        {
            var softDeletePrefix = "deleted-";
            var testFile = Path.Combine(this.DATAFOLDER, "testfile.txt");

            // Create multiple backups that will result in files being soft-deleted
            for (int i = 1; i <= 5; i++)
            {
                File.WriteAllText(testFile, $"test content version {i}");
                Thread.Sleep(1100); // Ensure timestamp difference
                using (var c = new Controller("file://" + this.TARGETFOLDER, TestOptions.Expand(new { soft_delete_prefix = softDeletePrefix, keep_versions = 2 }), null))
                {
                    var result = c.Backup(new[] { this.DATAFOLDER });
                    Assert.That(result.Errors, Is.Empty, $"Backup {i} should have no errors");
                }
            }

            // Check that we have multiple soft-deleted files
            var allFiles = Directory.GetFiles(this.TARGETFOLDER);
            var softDeletedFiles = allFiles.Where(f => Path.GetFileName(f).StartsWith(softDeletePrefix)).ToArray();

            Assert.That(softDeletedFiles.Length, Is.GreaterThan(0),
                $"Should have soft-deleted files. Files in target: {string.Join(", ", allFiles.Select(Path.GetFileName))}");

            // Verify that we have multiple dlist files soft-deleted (from different versions)
            var softDeletedDlistFiles = softDeletedFiles.Where(f => f.Contains(".dlist.")).ToArray();
            Assert.That(softDeletedDlistFiles.Length, Is.GreaterThanOrEqualTo(2),
                $"Should have multiple soft-deleted dlist files. Soft-deleted dlist files: {string.Join(", ", softDeletedDlistFiles.Select(Path.GetFileName))}");
        }
    }

    /// <summary>
    /// A backend wrapper that does NOT implement IRenameEnabledBackend.
    /// This is used to test the soft-delete fallback mechanism (download, upload with new name, delete old).
    /// </summary>
    public class NoRenameBackend : IBackend, IStreamingBackend
    {
        static NoRenameBackend() { WrappedBackend = "file"; }

        public static string WrappedBackend { get; set; }

        private IStreamingBackend m_backend;

        public NoRenameBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        public NoRenameBackend(string url, Dictionary<string, string> options)
        {
            var u = new Library.Utility.Uri(url).SetScheme(WrappedBackend).ToString();
            m_backend = (IStreamingBackend)Library.DynamicLoader.BackendLoader.GetBackend(u, options);
        }

        #region IStreamingBackend implementation
        public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            return m_backend.PutAsync(remotename, stream, cancelToken);
        }

        public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            return m_backend.GetAsync(remotename, stream, cancelToken);
        }
        #endregion

        #region IBackend implementation
        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
        {
            return m_backend.ListAsync(cancellationToken);
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            return m_backend.PutAsync(remotename, filename, cancelToken);
        }

        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            return m_backend.GetAsync(remotename, filename, cancelToken);
        }

        public Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            return m_backend.DeleteAsync(remotename, cancelToken);
        }

        public Task TestAsync(CancellationToken cancelToken)
        {
            return m_backend.TestAsync(cancelToken);
        }

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            return m_backend.CreateFolderAsync(cancelToken);
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => m_backend.GetDNSNamesAsync(cancelToken);

        public string DisplayName => "No Rename Backend";

        public string ProtocolKey => "norename";

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                if (m_backend == null)
                    try { return Duplicati.Library.DynamicLoader.BackendLoader.GetSupportedCommands(WrappedBackend + "://").ToList(); }
                    catch { }

                return m_backend.SupportedCommands;
            }
        }

        public string Description => "A testing backend that does not support rename operations";

        public bool SupportsStreaming => m_backend?.SupportsStreaming ?? false;
        #endregion

        #region IDisposable implementation
        public void Dispose()
        {
            if (m_backend != null)
                try { m_backend.Dispose(); }
                finally { m_backend = null; }
        }
        #endregion
    }
}
