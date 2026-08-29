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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that options belonging to a source provider are covered by the option
/// validation performed by <see cref="Controller"/>, so that an unknown option or an
/// invalid value produces a warning instead of being silently ignored.
/// </summary>
[TestFixture]
public class SourceProviderOptionValidationTests : BasicSetupHelper
{
    private sealed class LogSink : ILogDestination
    {
        public List<LogEntry> Entries { get; } = [];

        public void WriteMessage(LogEntry entry)
        {
            Entries.Add(entry);
        }
    }

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
    /// A source provider that advertises a flags option with a fixed set of valid values
    /// </summary>
    private sealed class FlagsOptionSourceProvider : ISourceProviderModule
    {
        /// <summary>
        /// The flags option advertised by this provider
        /// </summary>
        public const string FlagsOptionName = "test-provider-flags";

        private readonly string _mountPoint;

        /// <summary>
        /// Constructor used by the dynamic loader for module metadata
        /// </summary>
        public FlagsOptionSourceProvider()
        {
            _mountPoint = string.Empty;
        }

        /// <summary>
        /// Constructor used when instantiating the provider for an operation
        /// </summary>
        public FlagsOptionSourceProvider(string url, string mountPoint, Dictionary<string, string?> options)
        {
            _mountPoint = mountPoint;
        }

        public string Key => "test-flags-option";
        public string DisplayName => "Test flags option provider";
        public string Description => "Test provider that advertises a flags option";
        public IList<ICommandLineArgument> SupportedCommands =>
        [
            new CommandLineArgument(FlagsOptionName, CommandLineArgument.ArgumentType.Flags, "Test flags.", "Test flags.", null, null, ["Alpha", "Beta"])
        ];
        public string MountedPath => _mountPoint;
        public bool NeedsStoredMetadata => false;

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

    /// <summary>
    /// A restore destination provider that advertises a flags option with a fixed set of
    /// valid values, delegating the actual restore work to the local file system
    /// </summary>
    private sealed class FlagsOptionRestoreDestinationProvider : IRestoreDestinationProviderModule
    {
        /// <summary>
        /// The flags option advertised by this provider
        /// </summary>
        public const string FlagsOptionName = "test-restore-flags";

        /// <summary>
        /// The folder the provider restores to, set by the test before the restore starts
        /// </summary>
        public static string? TargetFolder { get; set; }

        private readonly IRestoreDestinationProvider? _inner;

        /// <summary>
        /// Constructor used by the dynamic loader for module metadata
        /// </summary>
        public FlagsOptionRestoreDestinationProvider()
        {
        }

        /// <summary>
        /// Constructor used when instantiating the provider for an operation
        /// </summary>
        public FlagsOptionRestoreDestinationProvider(string url, Dictionary<string, string> options)
            => _inner = new Library.SourceProvider.FileRestoreDestinationProvider(TargetFolder ?? string.Empty, true);

        public string Key => "test-restore-destination";
        public string DisplayName => "Test restore destination provider";
        public string Description => "Test provider that advertises a flags option";
        public IList<ICommandLineArgument> SupportedCommands =>
        [
            // Shared with FlagsOptionSourceProvider, mimicking providers that work in both roles
            new CommandLineArgument(FlagsOptionSourceProvider.FlagsOptionName, CommandLineArgument.ArgumentType.Flags, "Test flags.", "Test flags.", null, null, ["Alpha", "Beta"]),
            new CommandLineArgument(FlagsOptionName, CommandLineArgument.ArgumentType.Flags, "Test flags.", "Test flags.", null, null, ["Alpha", "Beta"])
        ];

        private IRestoreDestinationProvider Inner
            => _inner ?? throw new InvalidOperationException("The module metadata instance does not perform restore operations");

        public string TargetDestination => Inner.TargetDestination;
        public Task Initialize(CancellationToken cancel) => Inner.Initialize(cancel);
        public Task Finalize(Action<double>? progressCallback, CancellationToken cancel) => Inner.Finalize(progressCallback, cancel);
        public Task Test(CancellationToken cancellationToken) => Inner.Test(cancellationToken);
        public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel) => Inner.CreateFolderIfNotExists(path, cancel);
        public Task<bool> FileExists(string path, CancellationToken cancel) => Inner.FileExists(path, cancel);
        public Task<Stream> OpenWrite(string path, CancellationToken cancel) => Inner.OpenWrite(path, cancel);
        public Task<Stream> OpenRead(string path, CancellationToken cancel) => Inner.OpenRead(path, cancel);
        public Task<Stream> OpenReadWrite(string path, CancellationToken cancel) => Inner.OpenReadWrite(path, cancel);
        public Task<long> GetFileLength(string path, CancellationToken cancel) => Inner.GetFileLength(path, cancel);
        public Task<bool> HasReadOnlyAttribute(string path, CancellationToken cancel) => Inner.HasReadOnlyAttribute(path, cancel);
        public Task ClearReadOnlyAttribute(string path, CancellationToken cancel) => Inner.ClearReadOnlyAttribute(path, cancel);
        public Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool restoreSymlinkMetadata, bool restorePermissions, CancellationToken cancel) => Inner.WriteMetadata(path, metadata, restoreSymlinkMetadata, restorePermissions, cancel);
        public Task DeleteFolder(string path, CancellationToken cancel) => Inner.DeleteFolder(path, cancel);
        public Task DeleteFile(string path, CancellationToken cancel) => Inner.DeleteFile(path, cancel);
        public IList<string> GetPriorityFiles() => Inner.GetPriorityFiles();
        public void Dispose() => _inner?.Dispose();
    }

    [Test]
    public async Task Backup_WarnsOnInvalidSourceProviderOptionValue()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new FlagsOptionSourceProvider());

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true",
            [FlagsOptionSourceProvider.FlagsOptionName] = "Typo"
        };

        var logSink = new LogSink();
        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var results = await c.BackupAsync(["@/testremote|test-flags-option://source"]);
            Assert.That(results.Errors.Count(), Is.EqualTo(0), "Backup should succeed despite the invalid option value");
        }

        Assert.That(logSink.Entries.Any(x => x.Id == "OptionValidationError" && (x.Message?.Contains(FlagsOptionSourceProvider.FlagsOptionName) ?? false)), Is.True,
            "An invalid source provider option value should produce a validation warning");
    }

    [Test]
    public async Task Backup_DoesNotWarnOnValidSourceProviderOption()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new FlagsOptionSourceProvider());

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true",
            [FlagsOptionSourceProvider.FlagsOptionName] = "Alpha,Beta"
        };

        var logSink = new LogSink();
        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var results = await c.BackupAsync(["@/testremote|test-flags-option://source"]);
            Assert.That(results.Errors.Count(), Is.EqualTo(0), "Backup should succeed");
            Assert.That(results.Warnings.Count(), Is.EqualTo(0), "Backup should not produce warnings");
        }

        Assert.That(logSink.Entries.Any(x => x.Id == "UnsupportedOption" && (x.Message?.Contains(FlagsOptionSourceProvider.FlagsOptionName) ?? false)), Is.False,
            "A known source provider option should not be reported as unsupported");
        Assert.That(logSink.Entries.Any(x => x.Id == "OptionValidationError" && (x.Message?.Contains(FlagsOptionSourceProvider.FlagsOptionName) ?? false)), Is.False,
            "A valid source provider option value should not produce a validation warning");
    }

    [Test]
    public async Task Backup_MultipleSourcesWithSameProvider_DoesNotWarnOnDuplicateOptions()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new FlagsOptionSourceProvider());

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true",
            [FlagsOptionSourceProvider.FlagsOptionName] = "Alpha"
        };

        var logSink = new LogSink();
        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var results = await c.BackupAsync(["@/remotea|test-flags-option://one", "@/remoteb|test-flags-option://two"]);
            Assert.That(results.Errors.Count(), Is.EqualTo(0), "Backup should succeed");
        }

        Assert.That(logSink.Entries.Any(x => x.Id == "DuplicateOption" && (x.Message?.Contains(FlagsOptionSourceProvider.FlagsOptionName) ?? false)), Is.False,
            "Options from a source provider used by multiple sources should not be reported as duplicates");
        Assert.That(logSink.Entries.Any(x => x.Id == "UnsupportedOption" && (x.Message?.Contains(FlagsOptionSourceProvider.FlagsOptionName) ?? false)), Is.False,
            "A known source provider option should not be reported as unsupported");
    }

    [Test]
    public async Task Backup_WarnsOnUnsupportedOptionInSourceUrl()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new FlagsOptionSourceProvider());

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true"
        };

        var logSink = new LogSink();
        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var results = await c.BackupAsync(["@/testremote|test-flags-option://source?unsupported-option=1"]);
            Assert.That(results.Errors.Count(), Is.EqualTo(0), "Backup should succeed despite the unsupported option");
        }

        Assert.That(logSink.Entries.Any(x => x.Id == "UnsupportedOption" && (x.Message?.Contains("unsupported-option") ?? false)), Is.True,
            "An unsupported option in the source url should produce a validation warning");
    }

    [Test]
    public async Task Backup_WarnsOnInvalidOptionValueInSourceUrl()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new FlagsOptionSourceProvider());

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true"
        };

        var logSink = new LogSink();
        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var results = await c.BackupAsync(["@/testremote|test-flags-option://source?" + FlagsOptionSourceProvider.FlagsOptionName + "=Typo"]);
            Assert.That(results.Errors.Count(), Is.EqualTo(0), "Backup should succeed despite the invalid option value");
        }

        Assert.That(logSink.Entries.Any(x => x.Id == "OptionValidationError" && (x.Message?.Contains(FlagsOptionSourceProvider.FlagsOptionName) ?? false)), Is.True,
            "An invalid option value in the source url should produce a validation warning");
    }

    [Test]
    public async Task Restore_WarnsOnInvalidRestoreDestinationProviderOptions()
    {
        Library.DynamicLoader.RestoreDestinationProviderLoader.AddSourceProvider(new FlagsOptionRestoreDestinationProvider());
        FlagsOptionRestoreDestinationProvider.TargetFolder = this.RESTOREFOLDER;

        File.WriteAllText(Path.Combine(this.DATAFOLDER, "file.txt"), "data");

        using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions) { ["no-encryption"] = "true" }, null))
            await c.BackupAsync([this.DATAFOLDER]);

        var restoreOptions = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true",
            ["restore-path"] = "@test-restore-destination://target?unsupported-option=1",
            [FlagsOptionRestoreDestinationProvider.FlagsOptionName] = "Typo"
        };

        var logSink = new LogSink();
        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (var c = new Controller("file://" + this.TARGETFOLDER, restoreOptions, null))
        {
            var results = await c.RestoreAsync(["*"]);
            Assert.That(results.Errors.Count(), Is.EqualTo(0), "Restore should succeed despite the invalid option value");
        }

        Assert.That(logSink.Entries.Any(x => x.Id == "OptionValidationError" && (x.Message?.Contains(FlagsOptionRestoreDestinationProvider.FlagsOptionName) ?? false)), Is.True,
            "An invalid restore destination option value should produce a validation warning");
        Assert.That(logSink.Entries.Any(x => x.Id == "UnsupportedOption" && (x.Message?.Contains("unsupported-option") ?? false)), Is.True,
            "An unsupported option in the restore destination url should produce a validation warning");
    }

    [Test]
    public async Task Backup_SourceBackendSameAsTargetBackend_DoesNotWarnOnDuplicateOptions()
    {
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "file.txt"), "data");

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true"
        };

        var logSink = new LogSink();
        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var results = await c.BackupAsync(["@/mplocal|file://" + this.DATAFOLDER]);
            Assert.That(results.Errors.Count(), Is.EqualTo(0), "Backup should succeed");
        }

        Assert.That(logSink.Entries.Any(x => x.Id == "DuplicateOption"), Is.False,
            "Options from the target backend that is also used as a source should not be reported as duplicates");
    }

    [Test]
    public async Task Backup_SourceAndRestoreDestinationSharingOptions_DoesNotWarnOnDuplicateOptions()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new FlagsOptionSourceProvider());
        Library.DynamicLoader.RestoreDestinationProviderLoader.AddSourceProvider(new FlagsOptionRestoreDestinationProvider());
        FlagsOptionRestoreDestinationProvider.TargetFolder = this.RESTOREFOLDER;

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["no-encryption"] = "true",
            ["restore-path"] = "@test-restore-destination://target",
            [FlagsOptionSourceProvider.FlagsOptionName] = "Alpha",
            [FlagsOptionRestoreDestinationProvider.FlagsOptionName] = "Beta"
        };

        var logSink = new LogSink();
        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink, LogMessageType.Warning);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var results = await c.BackupAsync(["@/testremote|test-flags-option://source"]);
            Assert.That(results.Errors.Count(), Is.EqualTo(0), "Backup should succeed");
        }

        Assert.That(logSink.Entries.Any(x => x.Id == "DuplicateOption"), Is.False,
            "An option shared by the source and restore destination providers should not be reported as duplicate");
        Assert.That(logSink.Entries.Any(x => x.Id == "UnsupportedOption"), Is.False,
            "Options from the source and restore destination providers should be supported");
        Assert.That(logSink.Entries.Any(x => x.Id == "OptionValidationError"), Is.False,
            "Valid option values should not produce validation warnings");
    }
}
