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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Snapshots.Windows;
using Duplicati.Library.SourceProvider.Builtin;
using Duplicati.Library.SourceProvider.Builtin.HyperV;
using Duplicati.Library.SourceProvider.Builtin.MSSQL;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests the virtual paths the Hyper-V and MSSQL source providers store their entries with
    /// </summary>
    [TestFixture]
    public class TestVirtualSourcePath
    {
        private static readonly char DS = Path.DirectorySeparatorChar;

        /// <summary>
        /// Joins path segments with the platform separator
        /// </summary>
        private static string P(params string[] segments) => string.Join(DS, segments);

        private sealed class TestEntry : ISourceProviderEntry
        {
            public bool IsFolder { get; set; }
            public bool IsMetaEntry => false;
            public bool IsRootEntry => false;
            public DateTime CreatedUtc => DateTime.UnixEpoch;
            public DateTime LastModificationUtc => DateTime.UnixEpoch;
            public string Path { get; set; } = string.Empty;
            public long Size => 0;
            public bool IsSymlink => false;
            public string? SymlinkTarget => null;
            public FileAttributes Attributes => IsFolder ? FileAttributes.Directory : FileAttributes.Normal;
            public bool IsBlockDevice => false;
            public bool IsCharacterDevice => false;
            public bool IsAlternateStream => false;
            public string? HardlinkTargetId => null;
            public List<TestEntry> Children { get; } = [];

            public Task<Stream> OpenRead(CancellationToken cancellationToken)
                => throw new NotImplementedException();

            public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
                => Task.FromResult(new Dictionary<string, string?> { { "win-ext:accessrules", "x" } });

            public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
                => Task.FromResult(false);

            public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach (var child in Children)
                    yield return child;
                await Task.CompletedTask;
            }
        }

        [Test]
        public void MountedPath_is_a_rooted_unc_style_path()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new HyperVSourceProvider().MountedPath, Is.EqualTo($"{DS}{DS}duplicati{DS}hyperv{DS}"));
                Assert.That(new MSSQLSourceProvider().MountedPath, Is.EqualTo($"{DS}{DS}duplicati{DS}mssql{DS}"));
                Assert.That(new HyperVSourceProvider().SourcePrefix, Is.EqualTo("%HYPERV%"));
                Assert.That(new MSSQLSourceProvider().SourcePrefix, Is.EqualTo("%MSSQL%"));
            });

            if (OperatingSystem.IsWindows())
                Assert.That(Path.IsPathRooted(new HyperVSourceProvider().MountedPath), Is.True);
        }

        [Test]
        public void GetDataPathName_uses_the_last_segment()
        {
            Assert.Multiple(() =>
            {
                Assert.That(VirtualSourcePath.GetDataPathName(P("C:", "Data", "master.mdf")), Is.EqualTo("master.mdf"));
                Assert.That(VirtualSourcePath.GetDataPathName(P("C:", "VMs", "Snapshots") + DS), Is.EqualTo("Snapshots"));
                Assert.That(VirtualSourcePath.GetDataPathName("C:" + DS), Is.EqualTo("C"), "the root of a drive is named by its letter");
            });
        }

        [Test]
        public void AssignNames_keeps_names_that_do_not_clash()
        {
            var names = VirtualSourcePath.AssignNames([
                (P("C:", "Program Files", "MSSQL", "DATA", "master.mdf"), false),
                (P("C:", "Program Files", "MSSQL", "DATA", "mastlog.ldf"), false),
            ]);

            Assert.That(names, Is.EqualTo(new[] { "master.mdf", "mastlog.ldf" }));
        }

        [Test]
        public void AssignNames_numbers_every_clashing_name_in_path_order()
        {
            // Listed out of order and with different casing; numbered by full path
            var names = VirtualSourcePath.AssignNames([
                (P("F:", "Data", "data.ndf"), false),
                (P("C:", "Data", "data.ndf"), false),
                (P("D:", "Data", "DATA.NDF"), false),
                (P("C:", "Data", "db.mdf"), false),
            ]);

            Assert.That(names, Is.EqualTo(new[] { "data-3.ndf", "data-1.ndf", "DATA-2.NDF", "db.mdf" }));
        }

        [Test]
        public void AssignNames_numbers_before_the_last_extension_and_not_on_folders()
        {
            var names = VirtualSourcePath.AssignNames([
                (P("C:", "a", "disk.part.vhdx"), false),
                (P("D:", "b", "disk.part.vhdx"), false),
                (P("C:", "a", "Snapshots.old") + DS, true),
                (P("D:", "b", "Snapshots.old") + DS, true),
            ]);

            Assert.That(names, Is.EqualTo(new[] { "disk.part-1.vhdx", "disk.part-2.vhdx", "Snapshots.old-1", "Snapshots.old-2" }));
        }

        [Test]
        public void AssignNames_skips_numbers_that_are_already_in_use()
        {
            var names = VirtualSourcePath.AssignNames([
                (P("C:", "Data", "data.ndf"), false),
                (P("D:", "Data", "data.ndf"), false),
                (P("E:", "Data", "data-1.ndf"), false),
            ]);

            Assert.That(names, Is.EqualTo(new[] { "data-2.ndf", "data-3.ndf", "data-1.ndf" }));
        }

        [Test]
        public void RemoveNestedPaths_drops_duplicates_and_contained_paths()
        {
            var result = VirtualSourcePath.RemoveNestedPaths([
                P("C:", "VMs", "a") + DS,
                P("C:", "VMs", "a", "disk.vhdx"),
                P("C:", "VMs", "a"),
                P("C:", "VMs", "ab.vhdx"),
                P("D:", "x.vhdx"),
                P("D:", "x.vhdx"),
            ]);

            Assert.That(result, Is.EqualTo(new[] { P("C:", "VMs", "a") + DS, P("C:", "VMs", "ab.vhdx"), P("D:", "x.vhdx") }));
        }

        [Test]
        public async Task VirtualMappedEntry_records_the_original_path_on_every_entry()
        {
            var vm = $"{DS}{DS}duplicati{DS}hyperv{DS}abc{DS}";
            var folder = new TestEntry { IsFolder = true, Path = P("C:", "VMs") + DS };
            folder.Children.Add(new TestEntry { Path = P("C:", "VMs", "disk.vhdx") });

            var mapped = VirtualMappedEntry.MapDataPaths(vm, [folder], "hyperv:", "1", new Dictionary<string, string?> { { "hyperv:vm-id", "abc" } }).Single();
            var child = (await mapped.Enumerate(CancellationToken.None).ToListAsync()).Single();
            var folderMeta = await mapped.GetMinorMetadata(CancellationToken.None);
            var childMeta = await child.GetMinorMetadata(CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(mapped.Path, Is.EqualTo(vm + "VMs" + DS));
                Assert.That(child.Path, Is.EqualTo(vm + P("VMs", "disk.vhdx")));
                Assert.That(folderMeta["hyperv:orig-path"], Is.EqualTo(P("C:", "VMs") + DS));
                Assert.That(folderMeta["hyperv:Type"], Is.EqualTo("Folder"));
                Assert.That(childMeta["hyperv:orig-path"], Is.EqualTo(P("C:", "VMs", "disk.vhdx")));
                Assert.That(childMeta["hyperv:vm-id"], Is.EqualTo("abc"));
                Assert.That(childMeta["hyperv:v"], Is.EqualTo("1"));
                Assert.That(childMeta["hyperv:Type"], Is.EqualTo("File"));
                Assert.That(childMeta["win-ext:accessrules"], Is.EqualTo("x"), "the file's own metadata is kept");
            });
        }

        [Test]
        public void MapDataPaths_places_clashing_data_paths_under_numbered_names()
        {
            var db = $"{DS}{DS}duplicati{DS}mssql{DS}SRV{DS}MSSQLSERVER{DS}Sales{DS}";
            var mapped = VirtualMappedEntry.MapDataPaths(db, [
                new TestEntry { Path = P("E:", "Data", "data.ndf") },
                new TestEntry { Path = P("D:", "Data", "data.ndf") },
                new TestEntry { Path = P("D:", "Data", "Sales.mdf") },
            ], "mssql:", "1", new Dictionary<string, string?>());

            Assert.That(mapped.Select(x => x.Path), Is.EqualTo(new[] { db + "data-2.ndf", db + "data-1.ndf", db + "Sales.mdf" }));
        }

        [Test]
        public async Task FindEntry_resolves_entries_inside_a_data_path_folder()
        {
            var vm = $"{DS}{DS}duplicati{DS}hyperv{DS}abc{DS}";
            var folder = new TestEntry { IsFolder = true, Path = P("C:", "VMs") + DS };
            folder.Children.Add(new TestEntry { Path = P("C:", "VMs", "disk.vhdx") });
            var root = VirtualMappedEntry.MapDataPaths(vm, [folder], "hyperv:", "1", new Dictionary<string, string?>()).Single();

            var file = await VirtualSourcePath.FindEntryAsync(root, vm + P("VMs", "disk.vhdx"), false, CancellationToken.None);
            var self = await VirtualSourcePath.FindEntryAsync(root, vm + "VMs", true, CancellationToken.None);
            var wrongKind = await VirtualSourcePath.FindEntryAsync(root, vm + P("VMs", "disk.vhdx"), true, CancellationToken.None);
            var missing = await VirtualSourcePath.FindEntryAsync(root, vm + P("VMs", "nope.vhdx"), false, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(file?.Path, Is.EqualTo(vm + P("VMs", "disk.vhdx")));
                Assert.That(self, Is.SameAs(root));
                Assert.That(wrongKind, Is.Null);
                Assert.That(missing, Is.Null);
            });
        }

        [Test]
        public async Task MSSQL_tree_always_has_an_instance_level()
        {
            var dbs = new List<MSSQLDB>
            {
                new() { Server = "SRV", InstanceId = "", Database = "master", DataPaths = [] },
                new() { Server = "SRV", InstanceId = "SQLEXPRESS", Database = "master", DataPaths = [] },
            };

            var root = new MSSQLRootEntry(new MSSQLSourceProvider().MountedPath, dbs, null);
            var server = (await root.Enumerate(CancellationToken.None).ToListAsync()).Single();
            var instances = await server.Enumerate(CancellationToken.None).ToListAsync();
            var defaultDbs = await instances.First().Enumerate(CancellationToken.None).ToListAsync();

            Assert.Multiple(() =>
            {
                Assert.That(server.Path, Is.EqualTo($"{DS}{DS}duplicati{DS}mssql{DS}SRV{DS}"));
                Assert.That(instances.Select(x => x.Path), Is.EqualTo(new[] {
                    $"{DS}{DS}duplicati{DS}mssql{DS}SRV{DS}MSSQLSERVER{DS}",
                    $"{DS}{DS}duplicati{DS}mssql{DS}SRV{DS}SQLEXPRESS{DS}",
                }));
                Assert.That(defaultDbs.Single().Path, Is.EqualTo($"{DS}{DS}duplicati{DS}mssql{DS}SRV{DS}MSSQLSERVER{DS}master{DS}"));
            });
        }

        [Test]
        public void HyperV_source_paths_translate_to_the_stored_paths()
        {
            var provider = new HyperVSourceProvider();
            var root = provider.MountedPath;

            Assert.Multiple(() =>
            {
                Assert.That(provider.TranslateSourcePath("%HYPERV%"), Is.EqualTo(root));
                Assert.That(provider.TranslateSourcePath(@"%HYPERV%\abc"), Is.EqualTo(root + "abc" + DS));
                Assert.That(provider.TranslateSourcePath(@"%HYPERV%\abc\"), Is.EqualTo(root + "abc" + DS));
                Assert.That(provider.TranslateSourcePath(@"%HYPERV%\abc\C:\VMs\disk.vhdx"), Is.Null, "a local path below a machine is not translated");
                Assert.That(provider.TranslateSourcePath(@"%MSSQL%\abc"), Is.Null);
            });
        }

        [Test]
        public void MSSQL_source_paths_translate_to_the_stored_paths()
        {
            var provider = new MSSQLSourceProvider();
            var root = provider.MountedPath;

            Assert.Multiple(() =>
            {
                Assert.That(provider.TranslateSourcePath("%MSSQL%"), Is.EqualTo(root));
                Assert.That(provider.TranslateSourcePath(@"%MSSQL%\SRV"), Is.EqualTo(root + "SRV" + DS));
                Assert.That(provider.TranslateSourcePath(@"%MSSQL%\SRV\tempdb"), Is.EqualTo(root + P("SRV", "MSSQLSERVER", "tempdb") + DS), "two segments without a separator is a database on the default instance");
                Assert.That(provider.TranslateSourcePath(@"%MSSQL%\SRV\SQLEXPRESS\"), Is.EqualTo(root + P("SRV", "SQLEXPRESS") + DS), "two segments with a separator is an instance");
                Assert.That(provider.TranslateSourcePath(@"%MSSQL%\SRV\MSSQLSERVER"), Is.EqualTo(root + P("SRV", "MSSQLSERVER") + DS), "the default instance is always an instance");
                Assert.That(provider.TranslateSourcePath(@"%MSSQL%\SRV\INST\HR"), Is.EqualTo(root + P("SRV", "INST", "HR") + DS));
                Assert.That(provider.TranslateSourcePath(@"%MSSQL%\SRV\INST\HR\data.mdf"), Is.Null);
            });
        }

        [Test]
        public void Prefixed_filters_match_the_stored_paths()
        {
            var mssql = new MSSQLSourceProvider().MountedPath;
            var hyperv = new HyperVSourceProvider().MountedPath;
            var filter = FilterExpression.Deserialize([
                @"-%MSSQL%\SRV\tempdb",
                @"-%MSSQL%\SRV\INST\",
                @"-%HYPERV%\abc",
                @"-%HYPERV%\d*",
            ])!;

            var translated = SourceProviderFactory.TranslatePrefixedFilters(filter)!;

            bool Excludes(string path) => translated.Matches(path, out var include, out _) && !include;
            Assert.Multiple(() =>
            {
                Assert.That(Excludes(mssql + P("SRV", "MSSQLSERVER", "tempdb") + DS), Is.True);
                Assert.That(Excludes(mssql + P("SRV", "MSSQLSERVER", "master") + DS), Is.False);
                Assert.That(Excludes(mssql + P("SRV", "INST") + DS), Is.True);
                Assert.That(Excludes(hyperv + "abc" + DS), Is.True);
                Assert.That(Excludes(hyperv + "def" + DS), Is.True, "wildcard");
                Assert.That(Excludes(hyperv + "xyz" + DS), Is.False);
            });
        }

        [Test]
        public void Filters_without_a_prefix_are_kept()
        {
            var filter = FilterExpression.Deserialize([@"-*.ldf", @"-[.*%MSSQL%.*]"])!;
            Assert.That(SourceProviderFactory.TranslatePrefixedFilters(filter), Is.SameAs(filter));
        }
    }
}
