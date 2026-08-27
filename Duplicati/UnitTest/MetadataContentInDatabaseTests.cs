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
using System.Runtime.CompilerServices;
using System.Threading;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Duplicati.UnitTest;

[TestFixture]
public class MetadataContentInDatabaseTests : BasicSetupHelper
{
    /// <summary>
    /// A source provider entry used by <see cref="MetadataRequiringSourceProvider"/>
    /// </summary>
    private sealed class TestProviderEntry : ISourceProviderEntry
    {
        public bool IsFolder { get; set; }
        public bool IsMetaEntry => false;
        public bool IsRootEntry { get; set; }
        public DateTime CreatedUtc => new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateTime LastModificationUtc => new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public bool IsSymlink => false;
        public string? SymlinkTarget => null;
        public FileAttributes Attributes => IsFolder ? FileAttributes.Directory : FileAttributes.Normal;
        public bool IsBlockDevice => false;
        public bool IsCharacterDevice => false;
        public bool IsAlternateStream => false;
        public string? HardlinkTargetId => null;
        public byte[]? Content { get; set; }
        public ISourceProviderEntry? Child { get; set; }

        public Task<Stream> OpenRead(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream(Content ?? []));

        public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>());

        public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            if (Child != null)
                yield return Child;
        }
    }

    /// <summary>
    /// A source provider that requires the "store-metadata-content-in-database" option,
    /// mimicking the Office365 and Google Workspace providers
    /// </summary>
    private sealed class MetadataRequiringSourceProvider : ISourceProviderModule
    {
        private readonly string _mountPoint;

        /// <summary>
        /// Constructor used by the dynamic loader for module metadata
        /// </summary>
        public MetadataRequiringSourceProvider()
        {
            _mountPoint = string.Empty;
        }

        /// <summary>
        /// Constructor used when instantiating the provider for an operation
        /// </summary>
        public MetadataRequiringSourceProvider(string url, string mountPoint, Dictionary<string, string?> options)
        {
            _mountPoint = mountPoint;
            if (!Library.Utility.Utility.ParseBoolOption(options, "store-metadata-content-in-database"))
                throw new UserInformationException("The option --store-metadata-content-in-database is required for this source", "DatabaseMetadataStorageNotEnabled");
        }

        public string Key => "test-metadata-requiring";
        public string DisplayName => "Test metadata requiring provider";
        public string Description => "Test provider that requires metadata to be stored in the database";
        public IList<ICommandLineArgument> SupportedCommands => [];
        public string MountedPath => _mountPoint;
        public bool NeedsStoredMetadata => true;

        public Task InitializeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task TestAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var content = "data"u8.ToArray();
            yield return new TestProviderEntry
            {
                Path = _mountPoint,
                IsFolder = true,
                IsRootEntry = true,
                Child = new TestProviderEntry
                {
                    Path = _mountPoint + "file.txt",
                    IsFolder = false,
                    Size = content.Length,
                    Content = content
                }
            };
        }

        public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
            => Task.FromResult<ISourceProviderEntry?>(null);

        public void Dispose()
        {
        }
    }

    private static long CountMetadatasetsWithContent(string dbPath)
    {
        using var db = SQLiteLoader.LoadConnection(dbPath);
        using var cmd = db.CreateCommand();

        return cmd.ExecuteScalarInt64(@"
            SELECT COUNT(*)
            FROM ""Metadataset""
            WHERE ""Content"" IS NOT NULL AND ""Content"" != ''
        ");
    }

    [Test]
    public async Task Backup_StoresMetadataContent_WhenOptionEnabledAsync()
    {
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true",
            ["store-metadata-content-in-database"] = "true"
        };

        Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "folder"));
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "folder", "file.txt"), "data");

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

        Assert.That(CountMetadatasetsWithContent(options["dbpath"]), Is.GreaterThan(0));
    }

    [Test]
    public async Task Recreate_StoresMetadataContent_WhenOptionEnabledAsync()
    {
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true",
            ["store-metadata-content-in-database"] = "true"
        };

        Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "folder"));
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "folder", "file.txt"), "data");

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

        // Force a recreate by deleting the local database.
        File.Delete(options["dbpath"]);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RepairAsync());

        Assert.That(CountMetadatasetsWithContent(options["dbpath"]), Is.GreaterThan(0));
    }

    [Test]
    public async Task Backup_AutoEnablesMetadataStorage_WhenSourceProviderRequiresItAsync()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new MetadataRequiringSourceProvider());

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true"
        };

        // The source provider requires "store-metadata-content-in-database",
        // which the controller should enable automatically
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync(["@/testremote|test-metadata-requiring://source"]));

        Assert.That(CountMetadatasetsWithContent(options["dbpath"]), Is.GreaterThan(0));
    }

    [Test]
    public void Backup_DoesNotAutoEnableMetadataStorage_WhenOptionExplicitlyDisabled()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new MetadataRequiringSourceProvider());

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true",
            ["store-metadata-content-in-database"] = "false"
        };

        // The user has explicitly set the option, so it is not overridden,
        // and the source provider refuses to load
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            Assert.ThrowsAsync<UserInformationException>(async () => await c.BackupAsync(["@/testremote|test-metadata-requiring://source"]));
    }

    [Test]
    public void EnableMetadataStorageIfRequiredBySources_SetsOption_WhenProviderRequiresIt()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new MetadataRequiringSourceProvider());

        var options = new Dictionary<string, string?>();
        var enabled = Library.Main.Operation.Common.SourceProviderFactory.EnableMetadataStorageIfRequiredBySources(
            ["/localfolder/", "@/testremote|test-metadata-requiring://source"], options);

        Assert.That(enabled, Is.True);
        Assert.That(options["store-metadata-content-in-database"], Is.EqualTo("true"));
    }

    [Test]
    public void EnableMetadataStorageIfRequiredBySources_DoesNotOverride_WhenOptionAlreadySet()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new MetadataRequiringSourceProvider());

        var options = new Dictionary<string, string?>
        {
            ["store-metadata-content-in-database"] = "false"
        };
        var enabled = Library.Main.Operation.Common.SourceProviderFactory.EnableMetadataStorageIfRequiredBySources(
            ["@/testremote|test-metadata-requiring://source"], options);

        Assert.That(enabled, Is.False);
        Assert.That(options["store-metadata-content-in-database"], Is.EqualTo("false"));
    }

    [Test]
    public void EnableMetadataStorageIfRequiredBySources_IgnoresLocalAndUnknownSources()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new MetadataRequiringSourceProvider());

        var options = new Dictionary<string, string?>();
        var enabled = Library.Main.Operation.Common.SourceProviderFactory.EnableMetadataStorageIfRequiredBySources(
            ["/localfolder/", "@/testremote|file:///somefolder", "@/testremote|unknown-scheme://source"], options);

        Assert.That(enabled, Is.False);
        Assert.That(options, Does.Not.ContainKey("store-metadata-content-in-database"));
    }
}
