using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class DirectListHandlerTests : BasicSetupHelper
    {
        [Test]
        public void ListFilesets()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["upload-unchanged-backups"] = "true"
            };

            for (var i = 0; i < 3; i++)
            {
                using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
                {
                    TestUtils.AssertResults(c.Backup(new[] { this.DATAFOLDER }));
                    var sets = c.ListFilesets();
                    Assert.That(sets.Filesets.Count(), Is.EqualTo(i + 1));
                }
            }
        }

        [Test]
        public void ListFolderContents()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["upload-unchanged-backups"] = "true"
            };

            var initial = new[] {
                "folder1/",
                "folder2/",
                "file1",
                "folder1/file1.txt",
                "folder1/file2.txt",
                "folder1/folder1/",
                "folder1/folder2/",
                "folder1/folder1/file1.txt",
                "folder1/folder2/file2.txt",
                "folder2/folder2/"
            };

            var add2 = new[] {
                "folder3/",
                "folder1/folder1/file3.txt",
            };

            var add3 = new[] {
                "folder1/folder1/file3.txt",
                "folder1/folder1/file4.txt"
            };

            IEnumerable<string> rootItems(IEnumerable<string> items)
                => items.Where(x => !x.Contains('/') || x.IndexOf('/') == x.Length - 1);

            void createStructure(IEnumerable<string> paths)
            {
                foreach (var p in paths)
                {
                    var path = Path.Combine(this.DATAFOLDER, p.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    if (p.EndsWith("/"))
                        Directory.CreateDirectory(path);
                    else
                        File.WriteAllText(path, p);
                }
            }

            var sources = initial.Take(3).ToArray();

            var rounds = new[] {
                initial,
                initial.Concat(add2),
                initial.Concat(add3)
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                foreach (var round in rounds)
                {
                    createStructure(round);
                    TestUtils.AssertResults(c.Backup(rootItems(round).Select(x => Path.Combine(this.DATAFOLDER, x.Replace("/", Path.DirectorySeparatorChar.ToString()))).ToArray()));
                }

                var sets = c.ListFilesets();
                Assert.That(sets.Filesets.Count(), Is.EqualTo(rounds.Length));
            }

            var version = 0;
            foreach (var round in rounds.Reverse())
            {
                using (var c = new Controller("file://" + this.TARGETFOLDER, options.Expand(new { version = version }), null))
                {
                    var files = c.ListFolder([""], 0, 0);
                    var roots = rootItems(round).Select(x => Path.Combine(this.DATAFOLDER, x.Replace("/", Path.DirectorySeparatorChar.ToString()))).ToArray();

                    Assert.That(files.Entries.Items.Count(), Is.EqualTo(roots.Length));
                    Assert.That(files.Entries.Items.Count(x => x.IsDirectory), Is.EqualTo(roots.Count(x => x.EndsWith(Path.DirectorySeparatorChar))));
                    Assert.That(files.Entries.Items.Count(x => !x.IsDirectory), Is.EqualTo(roots.Count(x => !x.EndsWith(Path.DirectorySeparatorChar))));
                    Assert.That(files.Entries.Items.Count(x => roots.Any(y => x.Path.EndsWith(y))), Is.EqualTo(roots.Length));

                    var work = new Queue<string>(roots);
                    while (work.Count > 0)
                    {
                        var path = work.Dequeue();
                        var files2 = c.ListFolder(new[] { Path.Combine(this.DATAFOLDER, path.Replace("/", Path.DirectorySeparatorChar.ToString())) }, 0, 0);
                        var matches = round.Select(x => Path.Combine(this.DATAFOLDER, x.Replace("/", Path.DirectorySeparatorChar.ToString())))
                            .Where(x => x.StartsWith(path) && x.Length > path.Length && !x.Substring(path.Length, x.Length - path.Length - 1).Contains(Path.DirectorySeparatorChar))
                            .ToArray();

                        Assert.That(files2.Entries.Items.Count(), Is.EqualTo(matches.Length));
                        Assert.That(files2.Entries.Items.Count(x => x.IsDirectory), Is.EqualTo(matches.Count(x => x.EndsWith(Path.DirectorySeparatorChar))));
                        Assert.That(files2.Entries.Items.Count(x => !x.IsDirectory), Is.EqualTo(matches.Count(x => !x.EndsWith(Path.DirectorySeparatorChar))));
                        Assert.That(files2.Entries.Items.Count(x => matches.Contains(x.Path)), Is.EqualTo(matches.Length));

                        foreach (var match in matches)
                            if (match.EndsWith("/"))
                                work.Enqueue(match);
                    }
                }
                version++;
            }
        }

        [Test]
        public void ListFileVersions_LifecycleTest()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["upload-unchanged-backups"] = "true"
            };

            var stays = "file_stays.txt";
            var modify = "file_modify.txt";
            var delete = "file_delete.txt";
            var create = "file_create.txt";

            void createStructure(IEnumerable<string> files)
            {
                foreach (var f in files)
                {
                    var path = Path.Combine(this.DATAFOLDER, f.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, f + "-contents");
                }
            }

            void modifyFile(string file, string text)
            {
                var path = Path.Combine(this.DATAFOLDER, file.Replace("/", Path.DirectorySeparatorChar.ToString()));
                File.WriteAllText(path, text);
            }

            var rounds = new List<string[]> {
                new[] { stays, modify, delete }, // Round 0 - initial
                new[] { stays, modify, create }, // Round 1 - modify, create, delete 'delete'
                new[] { stays, modify, create }  // Round 2 - no changes
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                // Round 0 - initial
                createStructure(rounds[0]);
                TestUtils.AssertResults(c.Backup(rounds[0].Select(x => Path.Combine(this.DATAFOLDER, x.Replace("/", Path.DirectorySeparatorChar.ToString()))).ToArray()));

                // Round 1 - modify and create new file
                modifyFile(modify, "modified-content");
                createStructure(new[] { create });
                File.Delete(Path.Combine(this.DATAFOLDER, delete.Replace("/", Path.DirectorySeparatorChar.ToString()))); // Delete file
                TestUtils.AssertResults(c.Backup(rounds[1].Select(x => Path.Combine(this.DATAFOLDER, x.Replace("/", Path.DirectorySeparatorChar.ToString()))).ToArray()));

                // Round 2 - no changes
                TestUtils.AssertResults(c.Backup(rounds[2].Select(x => Path.Combine(this.DATAFOLDER, x.Replace("/", Path.DirectorySeparatorChar.ToString()))).ToArray()));
            }

            var pathsToCheck = new[]
            {
                Path.Combine(this.DATAFOLDER, stays),
                Path.Combine(this.DATAFOLDER, modify),
                Path.Combine(this.DATAFOLDER, delete),
                Path.Combine(this.DATAFOLDER, create)
            };

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var allVersions = c.ListFileVersions(pathsToCheck, 0, 0);
                var grouped = allVersions.FileVersions.Items.GroupBy(x => x.Path);

                foreach (var group in grouped)
                {
                    var path = group.Key;
                    var versions = group.OrderByDescending(x => x.Time).ToArray(); // Newest first

                    if (path.EndsWith(stays))
                        Assert.That(versions.Length, Is.EqualTo(3), $"File {path} should exist in all 3 versions");
                    else if (path.EndsWith(modify))
                        Assert.That(versions.Length, Is.EqualTo(3), $"File {path} should exist in all 3 versions");
                    else if (path.EndsWith(delete))
                        Assert.That(versions.Length, Is.EqualTo(1), $"File {path} should exist only in first version before deletion");
                    else if (path.EndsWith(create))
                        Assert.That(versions.Length, Is.EqualTo(2), $"File {path} should exist in 2 versions after creation");
                    else
                        Assert.Fail($"Unexpected file {path} in file versions list.");
                }
            }
        }

        [Test]
        public void SearchFilesTest()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["upload-unchanged-backups"] = "true"
            };

            var initial = new[] {
                "file1.txt",
                "folder1/",
                "folder1/file2.txt",
                "folder1/file3.txt",
                "folder2/",
                "folder2/notes.txt"
            };

            IEnumerable<string> rootItems(IEnumerable<string> items)
                => items.Where(x => !x.Contains('/') || x.IndexOf('/') == x.Length - 1);

            void createStructure(IEnumerable<string> paths)
            {
                foreach (var p in paths)
                {
                    var path = Path.Combine(this.DATAFOLDER, p.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    if (p.EndsWith("/"))
                        Directory.CreateDirectory(path);
                    else
                        File.WriteAllText(path, p);
                }
            }

            IFilter ParseFilters(string args)
                => args.Split(";").Select(x => (IFilter)new FilterExpression(x.Substring(1), x.StartsWith("+"))).Aggregate((x, y) => FilterExpression.Combine(x, y));

            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                createStructure(initial);
                TestUtils.AssertResults(c.Backup(rootItems(initial).Select(x => Path.Combine(this.DATAFOLDER, x.Replace("/", Path.DirectorySeparatorChar.ToString()))).ToArray()));

                // Simple Search for 'file'
                var search = c.SearchEntries(null, ParseFilters("+file"), 0, 0);
                Assert.That(search.FileVersions.Items.Count(), Is.EqualTo(3)); // file1.txt, file2.txt, file3.txt

                // Simple Search for 'notes'
                var searchNotes = c.SearchEntries(null, ParseFilters("+notes"), 0, 0);
                Assert.That(searchNotes.FileVersions.Items.Count(), Is.EqualTo(1));
                Assert.That(searchNotes.FileVersions.Items.First().Path.EndsWith("notes.txt"));

                // Simple Search for NOT 'notes'
                var searchNotNotes = c.SearchEntries(null, ParseFilters("-notes"), 0, 0);
                Assert.That(searchNotNotes.FileVersions.Items.Count(), Is.EqualTo(5));
                Assert.That(searchNotNotes.FileVersions.Items.All(x => !x.Path.Contains("notes")), Is.True);

                // Simple Search for 'folder1' folder contents
                var searchFolder = c.SearchEntries(null, ParseFilters("+folder1"), 0, 0);
                Assert.That(searchFolder.FileVersions.Items.All(x => x.Path.Contains("folder1")), Is.True);

                // Simple Search with exact file name
                var searchExact = c.SearchEntries(null, ParseFilters("+file1.txt"), 0, 0);
                Assert.That(searchExact.FileVersions.Items.Count(), Is.EqualTo(1));
                Assert.That(searchExact.FileVersions.Items.First().Path.EndsWith("file1.txt"));

                // Mixed include and exclude
                var searchMixed = c.SearchEntries(null, ParseFilters("+file;-file2"), 0, 0);
                // Include has precedence over exclude, but we include non-matches as well
                Assert.That(searchMixed.FileVersions.Items.Count(), Is.EqualTo(6));
                Assert.That(searchMixed.FileVersions.Items.Any(x => x.Path.Contains("file2")), Is.True);

                // Mixed include and exclude
                var searchMixed2 = c.SearchEntries(null, ParseFilters("+file2;-file"), 0, 0);
                // Include has precedence over exclude, but we include non-matches as well
                Assert.That(searchMixed2.FileVersions.Items.Count(), Is.EqualTo(4));
                Assert.That(searchMixed2.FileVersions.Items.Any(x => x.Path.Contains("file2")), Is.True);

                // Mixed include and exclude, wildcards
                var searchMixed3 = c.SearchEntries(null, ParseFilters("+file2;-*"), 0, 0);
                // Include has precedence over exclude, but we include non-matches as well
                Assert.That(searchMixed3.FileVersions.Items.Count(), Is.EqualTo(1));
                Assert.That(searchMixed3.FileVersions.Items.All(x => x.Path.Contains("file2")), Is.True);

                // NEW: Search inside specific folder prefix: folder1
                var searchInFolder1 = c.SearchEntries(
                    new[] { Path.Combine(this.DATAFOLDER, "folder1") },
                    ParseFilters("+file"),
                    0, 0);
                Assert.That(searchInFolder1.FileVersions.Items.Count(), Is.EqualTo(2)); // file2.txt, file3.txt inside folder1
                Assert.That(searchInFolder1.FileVersions.Items.All(x => x.Path.Contains("folder1")), Is.True);

                // NEW: Search inside multiple folder prefixes: folder1 and folder2
                var searchInFolders = c.SearchEntries(
                    new[]
                    {
                Path.Combine(this.DATAFOLDER, "folder1"),
                Path.Combine(this.DATAFOLDER, "folder2")
                    },
                    ParseFilters("+file;+notes"),
                    0, 0);
                Assert.That(searchInFolders.FileVersions.Items.Count(), Is.EqualTo(3)); // file2.txt, file3.txt, notes.txt
                Assert.That(searchInFolders.FileVersions.Items.Any(x => x.Path.Contains("folder1")), Is.True);
                Assert.That(searchInFolders.FileVersions.Items.Any(x => x.Path.Contains("folder2")), Is.True);
            }
        }

        [Test]
        public async Task GetRootPrefixes_ShouldReturnCorrectLinuxPrefixes()
        {
            // Arrange
            using var tempFile = new TempFile();
            using var db = await LocalListDatabase.CreateAsync(tempFile, 1);
            SeedTestData(db, [
                "/folder1/", // 1
                "/folder1/sub1/",
                "/folder2/", // 3
                "/folder2/sub2/",
                "/folder3/" // 5
            ]);

            // Act
            var result = await db.GetRootPrefixes(1).ToListAsync();

            // Assert
            Assert.That(result, Does.Contain(1L));
            Assert.That(result, Does.Contain(3L));
            Assert.That(result, Does.Contain(5L));
            Assert.That(result, Does.Not.Contain(2L));
            Assert.That(result, Does.Not.Contain(4L));
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task GetRootPrefixes_ShouldReturnCorrectWindowsDrivePrefixes()
        {
            // Arrange
            using var tempFile = new TempFile();
            using var db = await LocalListDatabase.CreateAsync(tempFile, 1);
            SeedTestData(db, [
                "C:\\folder1\\",
                "C:\\folder1\\sub1\\",
                "C:\\folder2\\",
                "D:\\otherfolder\\"
            ]);

            // Act
            var result = await db.GetRootPrefixes(1).ToListAsync();

            // Assert
            Assert.That(result, Does.Contain(1L));
            Assert.That(result, Does.Contain(3L));
            Assert.That(result, Does.Contain(4L));
            Assert.That(result, Does.Not.Contain(2L));
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task GetRootPrefixes_ShouldReturnCorrectWindowsUncPrefixes()
        {
            // Arrange
            using var tempFile = new TempFile();
            using var db = await LocalListDatabase.CreateAsync(tempFile, 1);
            SeedTestData(db, [
                "\\\\server\\share\\folder\\",
                "\\\\server\\share\\folder\\subfolder\\",
                "\\\\server\\share\\otherfolder\\"
            ]);

            // Act
            var result = await db.GetRootPrefixes(1).ToListAsync();

            // Assert
            Assert.That(result, Does.Contain(1L));
            Assert.That(result, Does.Contain(3L));
            Assert.That(result, Does.Not.Contain(2L));
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task GetRootPrefixes_ShouldHandleMixedWindowsDriveAndUncPaths()
        {
            // Arrange
            using var tempFile = new TempFile();
            using var db = await LocalListDatabase.CreateAsync(tempFile, 1);
            SeedTestData(db, [
                "C:\\data\\", // 1
                "C:\\data\\sub1\\",
                "C:\\music\\", // 3
                "D:\\videos\\", // 4
                "\\\\server\\share\\docs\\", // 5
                "\\\\server\\share\\docs\\subdoc\\",
                "\\\\server\\share\\pictures\\" // 7
            ]);

            // Act
            var result = await db.GetRootPrefixes(1).ToListAsync();

            // Assert
            Assert.That(result, Does.Contain(1L));
            Assert.That(result, Does.Contain(3L));
            Assert.That(result, Does.Contain(4L));
            Assert.That(result, Does.Contain(5L));
            Assert.That(result, Does.Contain(7L));
            Assert.That(result, Does.Not.Contain(2L));
            Assert.That(result, Does.Not.Contain(6L));
            Assert.That(result.Count, Is.EqualTo(5));
        }

        private void SeedTestData(LocalListDatabase db, IEnumerable<string> prefixes)
        {
            long filesetId = 1;
            long prefixId = 1;
            long fileId = 1;
            using var cmd = db.Connection.CreateCommand();

            cmd.SetCommandAndParameters(@"
        INSERT INTO Fileset (ID, OperationID, VolumeID, IsFullBackup, Timestamp)
        VALUES (@filesetId, 1, 1, 1, 0);")
                .SetParameterValue("@filesetId", filesetId)
                .ExecuteNonQuery();

            foreach (var prefix in prefixes)
            {
                cmd.SetCommandAndParameters(@"
                    INSERT INTO PathPrefix (ID, Prefix) VALUES (@prefixId, @prefix);
                    INSERT INTO FileLookup (ID, PrefixID, Path, BlocksetID, MetadataID)
                    VALUES (@fileId, @prefixId, @path, 1, 1);
                    INSERT INTO FilesetEntry (FilesetID, FileID, Lastmodified)
                    VALUES (@filesetId, @fileId, 0);"
                )
                .SetParameterValue("@prefixId", prefixId)
                .SetParameterValue("@prefix", prefix)
                .SetParameterValue("@fileId", fileId)
                .SetParameterValue("@filesetId", filesetId)
                .SetParameterValue("@path", prefix)
                .ExecuteNonQuery();

                prefixId++;
                fileId++;
            }
        }
    }
}