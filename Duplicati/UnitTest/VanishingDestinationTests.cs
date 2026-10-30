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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A destination folder that disappears during a backup, as a drive that is unplugged does, must
/// not be created again: the backup would carry on into a new, empty folder in its place, for
/// instance on the disk the drive was mounted on, and the destination would miss those files once
/// the drive is back. Creating the folder is only for a destination that was never there.
/// </summary>
public class VanishingDestinationTests : BasicSetupHelper
{
    /// <summary>
    /// Wraps the file backend, and moves the destination folder away just before the first upload
    /// </summary>
    public class VanishingFileBackend : IBackend
    {
        public const string Key = "vanishingfile";

        /// <summary>
        /// The destination folder
        /// </summary>
        public static string? Folder;

        /// <summary>
        /// The folder to move the destination to; nothing is moved when <c>null</c>
        /// </summary>
        public static string? MoveTo;

        private readonly IBackend? m_backend;

        public VanishingFileBackend()
        {
        }

        // ReSharper disable once UnusedMember.Global
        public VanishingFileBackend(string url, Dictionary<string, string> options)
        {
            var fileUrl = new Library.Utility.RelaxedUri(url).SetScheme("file").ToString();
            m_backend = Library.DynamicLoader.BackendLoader.GetBackend(fileUrl, options);
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            var moveTo = Interlocked.Exchange(ref MoveTo, null);
            if (moveTo != null)
                Directory.Move(Folder!, moveTo);
            return m_backend!.PutAsync(remotename, filename, cancelToken);
        }

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancelToken) => m_backend!.ListAsync(cancelToken);
        public Task GetAsync(string remotename, string filename, CancellationToken cancelToken) => m_backend!.GetAsync(remotename, filename, cancelToken);
        public Task DeleteAsync(string remotename, CancellationToken cancelToken) => m_backend!.DeleteAsync(remotename, cancelToken);
        public Task TestAsync(bool alsoWrite, CancellationToken cancelToken) => m_backend!.TestAsync(alsoWrite, cancelToken);
        public Task CreateFolderAsync(CancellationToken cancelToken) => m_backend!.CreateFolderAsync(cancelToken);
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(Array.Empty<string>());
        public string DisplayName => "Vanishing file backend";
        public string ProtocolKey => Key;
        public IList<ICommandLineArgument> SupportedCommands => [];
        public string Description => "A testing backend whose folder disappears before the first upload";
        public void Dispose() => m_backend?.Dispose();
    }

    private Dictionary<string, string> Options => TestOptions.Expand(new
    {
        no_encryption = true,
        number_of_retries = 2,
        retry_delay = "1s"
    });

    [Test]
    [Category("Targeted")]
    public async Task ABackupDoesNotRecreateADestinationThatDisappearsAsync()
    {
        File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), [1, 2, 3]);
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        Library.DynamicLoader.BackendLoader.AddBackend(new VanishingFileBackend());
        var moved = TARGETFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-unplugged";
        File.WriteAllBytes(Path.Combine(DATAFOLDER, "b"), [4, 5, 6]);
        VanishingFileBackend.Folder = TARGETFOLDER;
        VanishingFileBackend.MoveTo = moved;
        try
        {
            using (var c = new Controller(VanishingFileBackend.Key + "://" + TARGETFOLDER, Options, null))
                Assert.CatchAsync<Exception>(async () => await c.BackupAsync([DATAFOLDER]));

            Assert.That(VanishingFileBackend.MoveTo, Is.Null, "The backup did not upload anything");
            Assert.That(Directory.Exists(TARGETFOLDER), Is.False, "The destination was created again after it disappeared");
        }
        finally
        {
            VanishingFileBackend.MoveTo = null;
            if (Directory.Exists(moved))
            {
                if (Directory.Exists(TARGETFOLDER))
                    Directory.Delete(TARGETFOLDER, true);
                Directory.Move(moved, TARGETFOLDER);
            }
        }
    }
}
