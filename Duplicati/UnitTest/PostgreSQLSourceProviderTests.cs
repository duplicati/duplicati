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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.Builtin.PostgreSQL;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for the PostgreSQL source provider that need no server: the options, the client
/// program arguments, the archive reader against a recorded dump of each supported version,
/// the reading order of the dump, and the row segments.
/// The recorded dumps hold the schema <c>app</c> with <c>customers</c> (40 rows),
/// <c>orders</c> (120 rows) and <c>odd.name</c> (20 rows), <c>public.empty_table</c>,
/// and two large objects.
/// </summary>
[Category("PostgreSQL")]
public class PostgreSQLSourceProviderTests
{
    private static readonly string[] RecordedDumps = ["pg14.dump", "pg15.dump", "pg16.dump", "pg17.dump", "pg18.dump"];

    /// <summary>The archive format each version of pg_dump writes</summary>
    private static readonly Dictionary<string, string> ArchiveVersions = new()
    {
        ["pg14.dump"] = "1.14.0",
        ["pg15.dump"] = "1.14.0",
        ["pg16.dump"] = "1.15.0",
        ["pg17.dump"] = "1.16.0",
        ["pg18.dump"] = "1.16.0",
    };

    private const string Url = "postgresql://?postgresql-host=db.example.com&postgresql-database=app&postgresql-username=backup&postgresql-password=s3cret";

    private static byte[] LoadDump(string name)
    {
        var assembly = typeof(PostgreSQLSourceProviderTests).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith(name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// A dump that was recorded, or cut short, with the way the program ended
    /// </summary>
    private sealed class RecordedOutput(byte[] data, int exitCode, string errorOutput) : PostgreSQLClientTools.IDumpOutput
    {
        public string Tool => PostgreSQLClientTools.PgDump;
        public Stream Output { get; } = new MemoryStream(data, false);
        public Task<(int ExitCode, string ErrorOutput)> WaitForExitAsync(CancellationToken cancellationToken)
            => Task.FromResult((exitCode, errorOutput));
        public void Dispose() { }
    }

    private static PostgreSQLOptions Options(int blockSize)
        => PostgreSQLTestOptions.Parse(Url, new Dictionary<string, string?> { [PostgreSQLOptions.BlockSizeOption] = blockSize + "b" });

    private static async Task<PostgreSQLSourceEntry> OpenTreeAsync(byte[] dump, int exitCode, string errorOutput)
    {
        var session = await PostgreSQLDumpSession.StartAsync(new RecordedOutput(dump, exitCode, errorOutput), CancellationToken.None);
        return PostgreSQLSourceEntry.CreateTree(session, "/backup", Options(1024), new PostgreSQLSourceProvider.ServerInfo(170011, 12345), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "run-1");
    }

    private static async Task<List<ISourceProviderEntry>> ChildrenAsync(ISourceProviderEntry entry)
    {
        var result = new List<ISourceProviderEntry>();
        await foreach (var child in entry.Enumerate(CancellationToken.None))
            result.Add(child);
        return result;
    }

    private static async Task<byte[]> ReadAllAsync(ISourceProviderEntry entry)
    {
        using var stream = await entry.OpenRead(CancellationToken.None);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static string Rows(byte[] padded)
    {
        var length = PostgreSQLSegmenter.RemovePadding(padded);
        return Encoding.UTF8.GetString(padded, 0, length);
    }

    private static string TablePath(string name)
        => "/backup" + Path.DirectorySeparatorChar + PostgreSQLSourceProvider.TablesFolderName + Path.DirectorySeparatorChar + name;

    [Test]
    public void OptionsAreReadFromTheUrlBeforeTheBackupOptions()
    {
        var options = PostgreSQLTestOptions.Parse(Url + "&postgresql-port=6543", new Dictionary<string, string?>
        {
            [PostgreSQLOptions.HostOption] = "other.example.com",
            [PostgreSQLOptions.SslModeOption] = "verify-full",
            [PostgreSQLOptions.BlockSizeOption] = "64kb",
        });

        Assert.That(options.Host, Is.EqualTo("db.example.com"));
        Assert.That(options.Port, Is.EqualTo(6543));
        Assert.That(options.Database, Is.EqualTo("app"));
        Assert.That(options.Username, Is.EqualTo("backup"));
        Assert.That(options.Password, Is.EqualTo("s3cret"));
        Assert.That(options.SslMode, Is.EqualTo("verify-full"));
        Assert.That(options.IncludeLargeObjects, Is.False);
        Assert.That(options.BlockSize, Is.EqualTo(64 * 1024));
    }

    [Test]
    public void OptionsHaveDefaultsAndRejectBadValues()
    {
        var options = PostgreSQLTestOptions.Parse(Url, new Dictionary<string, string?>());
        Assert.That(options.Port, Is.EqualTo(PostgreSQLOptions.DefaultPort));
        Assert.That(options.SslMode, Is.EqualTo(PostgreSQLOptions.DefaultSslMode));
        Assert.That(options.BlockSize, Is.EqualTo(1024 * 1024));

        Assert.That(() => PostgreSQLTestOptions.Parse("postgresql://?postgresql-database=app&postgresql-username=u", new Dictionary<string, string?>()),
            Throws.InstanceOf<UserInformationException>(), "A missing host must be refused");
        Assert.That(() => PostgreSQLTestOptions.Parse(Url + "&postgresql-sslmode=sometimes", new Dictionary<string, string?>()),
            Throws.InstanceOf<UserInformationException>(), "An unknown TLS mode must be refused");
        Assert.That(() => PostgreSQLTestOptions.Parse(Url + "&postgresql-port=0", new Dictionary<string, string?>()),
            Throws.InstanceOf<UserInformationException>(), "An invalid port must be refused");
    }

    [Test]
    public void ARootCertificateGivenAsTextIsKeptInAFileWhileTheOptionsAreInUse()
    {
        const string pem = "-----BEGIN CERTIFICATE-----\nMIIBszCCAVmgAwIBAgIUexample\n-----END CERTIFICATE-----";

        string path;
        using (var options = PostgreSQLTestOptions.Parse(Url, new Dictionary<string, string?> { [PostgreSQLOptions.SslRootCertContentOption] = "\n" + pem + "\n\n" }))
        {
            path = options.SslRootCert!;
            Assert.That(File.ReadAllText(path), Is.EqualTo(pem + "\n"));

            // The client programs are pointed at the file, as they are for a certificate given as a path
            var psi = PostgreSQLClientTools.BuildStartInfo("psql", [], options);
            Assert.That(psi.Environment["PGSSLROOTCERT"], Is.EqualTo(path));
        }

        Assert.That(File.Exists(path), Is.False, "The file is removed with the options");

        using var withPath = PostgreSQLTestOptions.Parse(Url, new Dictionary<string, string?> { [PostgreSQLOptions.SslRootCertOption] = "/certs/root.crt" });
        Assert.That(withPath.SslRootCert, Is.EqualTo("/certs/root.crt"));

        Assert.That(() => PostgreSQLTestOptions.Parse(Url, new Dictionary<string, string?>
        {
            [PostgreSQLOptions.SslRootCertOption] = "/certs/root.crt",
            [PostgreSQLOptions.SslRootCertContentOption] = pem,
        }), Throws.InstanceOf<UserInformationException>(), "A certificate is given one way");

        Assert.That(() => PostgreSQLTestOptions.Parse(Url, new Dictionary<string, string?> { [PostgreSQLOptions.SslRootCertContentOption] = "/certs/root.crt" }),
            Throws.InstanceOf<UserInformationException>(), "A path is not a certificate");
    }

    [Test]
    public void ThePasswordIsOnlyInTheEnvironment()
    {
        var options = PostgreSQLTestOptions.Parse(Url, new Dictionary<string, string?>());
        var psi = PostgreSQLClientTools.BuildStartInfo("pg_dump", PostgreSQLClientTools.BuildDumpArguments(options, 17), options);

        Assert.That(psi.ArgumentList, Has.None.Contains("s3cret"));
        Assert.That(psi.Arguments, Does.Not.Contain("s3cret"));
        Assert.That(psi.Environment["PGPASSWORD"], Is.EqualTo("s3cret"));
        Assert.That(psi.Environment["PGHOST"], Is.EqualTo("db.example.com"));
        Assert.That(psi.Environment["PGSSLMODE"], Is.EqualTo("require"));
        Assert.That(psi.Environment["PGCLIENTENCODING"], Is.EqualTo("UTF8"));
        Assert.That(psi.ArgumentList, Is.EquivalentTo(new[] { "--format=custom", "--compress=0", "--no-password", "--encoding=UTF8", "--no-large-objects" }));

        var withLargeObjects = PostgreSQLTestOptions.Parse(Url + "&postgresql-include-large-objects=true", new Dictionary<string, string?>());
        Assert.That(PostgreSQLClientTools.BuildDumpArguments(withLargeObjects, 17), Has.None.EqualTo("--no-large-objects"));

        // pg_dump 14 and 15 know large objects as blobs
        foreach (var major in new[] { 14, 15 })
        {
            Assert.That(PostgreSQLClientTools.BuildDumpArguments(options, major), Has.Member("--no-blobs").And.None.EqualTo("--no-large-objects"));
            Assert.That(PostgreSQLClientTools.BuildDumpArguments(withLargeObjects, major), Has.None.EqualTo("--no-blobs"));
        }
        Assert.That(PostgreSQLClientTools.BuildDumpArguments(options, 16), Has.Member("--no-large-objects"));
    }

    [Test]
    public void TheClientProgramIsChosenByVersion()
    {
        Assert.That(PostgreSQLClientTools.ParseToolMajorVersion("pg_dump (PostgreSQL) 17.11 (Debian 17.11-1.pgdg13+2)"), Is.EqualTo(17));
        Assert.That(PostgreSQLClientTools.ParseToolMajorVersion("psql (PostgreSQL) 18.6"), Is.EqualTo(18));
        Assert.That(PostgreSQLClientTools.ParseToolMajorVersion("something else"), Is.Null);
        Assert.That(PostgreSQLClientTools.MajorFromServerVersionNum(170011), Is.EqualTo(17));
        Assert.That(PostgreSQLClientTools.MajorFromServerVersionNum(180006), Is.EqualTo(18));

        var options = PostgreSQLTestOptions.Parse(Url + "&postgresql-client-path=/opt/pg/bin", new Dictionary<string, string?>());
        Assert.That(PostgreSQLClientTools.LocateTool(PostgreSQLClientTools.PgDump, 17, options), Is.EqualTo(Path.Combine("/opt/pg/bin", "pg_dump")));
    }

    [Test]
    public void APackagedClientIsFoundByVersionInTheFirstFolderThatHasIt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var first = Path.Combine(root, "first", "{0}", "bin");
            var second = Path.Combine(root, "second", "pg@{0}", "bin");
            string Install(string folder, int major)
            {
                var path = Path.Combine(string.Format(folder, major), PostgreSQLClientTools.ExecutableFileName(PostgreSQLClientTools.PgDump));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, string.Empty);
                return path;
            }

            var first17 = Install(first, 17);
            var second17 = Install(second, 17);
            var second15 = Install(second, 15);

            Assert.That(PostgreSQLClientTools.FindPackagedTool(PostgreSQLClientTools.PgDump, 17, [first, second]), Is.EqualTo(first17));
            Assert.That(PostgreSQLClientTools.FindPackagedTool(PostgreSQLClientTools.PgDump, 17, [second, first]), Is.EqualTo(second17));
            Assert.That(PostgreSQLClientTools.FindPackagedTool(PostgreSQLClientTools.PgDump, 15, [first, second]), Is.EqualTo(second15));
            Assert.That(PostgreSQLClientTools.FindPackagedTool(PostgreSQLClientTools.PgDump, 16, [first, second]), Is.Null);
            Assert.That(PostgreSQLClientTools.FindPackagedTool(PostgreSQLClientTools.Psql, 17, [first, second]), Is.Null);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Test]
    public void TheMetadataIsKeptInTheDatabase()
    {
        using var provider = new PostgreSQLSourceProvider(Url, "/backup", PostgreSQLTestOptions.FromUrl(Url, new Dictionary<string, string?>()));
        Assert.That(provider.NeedsStoredMetadata, Is.True, "A client reads the kind of source from the stored metadata");
    }

    [Test]
    public void TableNamesAreStableAndEscaped()
    {
        Assert.That(PostgreSQLSourceProvider.TableEntryName("app", "orders"), Is.EqualTo("app.orders"));
        Assert.That(PostgreSQLSourceProvider.TableEntryName("app", "odd.name"), Is.EqualTo("app.odd%2Ename"));
        Assert.That(PostgreSQLSourceProvider.TableEntryName("a/b", "c%d"), Is.EqualTo("a%2Fb.c%25d"));

        foreach (var name in new[] { "odd.name", "a/b\\c", " lead and trail ", "æøå 100%", "tab\there" })
            Assert.That(PostgreSQLSourceProvider.UnescapeName(PostgreSQLSourceProvider.EscapeName(name)), Is.EqualTo(name));
    }

    [TestCaseSource(nameof(RecordedDumps))]
    public async Task TheArchiveOfEachSupportedVersionIsRead(string name)
    {
        var dump = LoadDump(name);
        var reader = await PostgreSQLArchiveReader.OpenAsync(new MemoryStream(dump, false), CancellationToken.None);

        Assert.That(reader.ArchiveVersion, Is.EqualTo(ArchiveVersions[name]));
        Assert.That(reader.ServerVersion, Does.StartWith(name.Substring(2, 2)));
        Assert.That(dump.AsSpan(0, reader.HeaderAndTableOfContents.Length).SequenceEqual(reader.HeaderAndTableOfContents), Is.True,
            "The table of contents entry must be the start of the archive, unchanged");

        var tables = reader.Entries
            .Where(x => x.HasData && x.Description == PostgreSQLArchiveReader.TableDataDescription)
            .Select(x => x.Namespace + "." + x.Tag)
            .ToList();
        Assert.That(tables, Is.EquivalentTo(new[] { "app.customers", "app.orders", "app.odd.name", "public.empty_table" }));
        Assert.That(reader.Entries.Single(x => x.Tag == "orders" && x.HasData).CopyStatement, Does.StartWith("COPY app.orders (id, customer_id, total, created) FROM stdin"));
        Assert.That(reader.Entries.Any(x => x.HasData && x.Description == PostgreSQLArchiveReader.LargeObjectsDescription), Is.True);

        // The first data block follows the table of contents directly
        var header = await reader.ReadBlockHeaderAsync(CancellationToken.None);
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.Value.Type, Is.EqualTo(PostgreSQLArchiveReader.BlockTypeData));
        Assert.That(reader.Entries.Any(x => x.DumpId == header.Value.DumpId && x.HasData), Is.True);
    }

    [Test]
    public void AnUnknownArchiveVersionIsRefused()
    {
        var dump = LoadDump(RecordedDumps[0]);
        dump[6] = 17;

        var ex = Assert.ThrowsAsync<UserInformationException>(() => PostgreSQLArchiveReader.OpenAsync(new MemoryStream(dump, false), CancellationToken.None));
        Assert.That(ex!.HelpID, Is.EqualTo("PostgreSQLUnsupportedArchiveVersion"));
    }

    [TestCaseSource(nameof(RecordedDumps))]
    public async Task TheEntriesAreReadInOrder(string name)
    {
        var dump = LoadDump(name);
        var root = await OpenTreeAsync(dump, 0, string.Empty);

        var top = await ChildrenAsync(root);
        Assert.That(top.Select(x => x.Path), Is.EqualTo(new[]
        {
            "/backup" + Path.DirectorySeparatorChar + PostgreSQLSourceProvider.TableOfContentsName,
            "/backup" + Path.DirectorySeparatorChar + PostgreSQLSourceProvider.TablesFolderName + Path.DirectorySeparatorChar,
            "/backup" + Path.DirectorySeparatorChar + PostgreSQLSourceProvider.LargeObjectsName,
        }));
        Assert.That(root, Is.InstanceOf<ISequentialSourceProviderEntry>());
        // The root tells a client that the backup holds a PostgreSQL database
        Assert.That((await root.GetMinorMetadata(CancellationToken.None))[PostgreSQLSourceProvider.MetadataExtendedType], Is.EqualTo("postgresql"));
        Assert.That(top[1], Is.InstanceOf<ISequentialSourceProviderEntry>());

        var toc = await ReadAllAsync(top[0]);
        Assert.That(dump.AsSpan(0, toc.Length).SequenceEqual(toc), Is.True);
        Assert.That(top[0].Size, Is.EqualTo(toc.Length));

        var tables = await ChildrenAsync(top[1]);
        Assert.That(tables.Select(x => x.Path), Is.EquivalentTo(new[] { TablePath("app.customers"), TablePath("app.orders"), TablePath("app.odd%2Ename"), TablePath("public.empty_table") }));

        var rowCounts = new Dictionary<string, int>();
        foreach (var table in tables)
        {
            Assert.That(table.Size, Is.EqualTo(-1), "The size of a table is not known before it is read");
            var rows = Rows(await ReadAllAsync(table));
            Assert.That(rows, Does.EndWith("\\.\n\n\n"), "The rows end with the end marker of COPY");
            rowCounts[table.Path] = rows.Split('\n').Length - 4;

            if (table.Path == TablePath("app.customers"))
                Assert.That(rows, Does.Contain("3\tcustomer 3\tline\\twith tab\\\\and\\nnewline 3\n"));

            var metadata = await table.GetMinorMetadata(CancellationToken.None);
            Assert.That(metadata[PostgreSQLSourceProvider.MetadataRunId], Is.EqualTo("run-1"));
            Assert.That(metadata[PostgreSQLSourceProvider.MetadataSegmentScheme], Is.EqualTo(PostgreSQLSegmenter.SchemeName));
        }

        Assert.That(rowCounts[TablePath("app.customers")], Is.EqualTo(40));
        Assert.That(rowCounts[TablePath("app.orders")], Is.EqualTo(120));
        Assert.That(rowCounts[TablePath("app.odd%2Ename")], Is.EqualTo(20));
        Assert.That(rowCounts[TablePath("public.empty_table")], Is.EqualTo(0));

        // The large objects are the rest of the archive, from the header of their first block
        var largeObjects = await ReadAllAsync(top[2]);
        Assert.That(largeObjects.Length, Is.GreaterThan(300));
        Assert.That(largeObjects[0], Is.EqualTo(PostgreSQLArchiveReader.BlockTypeLargeObjects));
        Assert.That(dump.AsSpan(dump.Length - largeObjects.Length).SequenceEqual(largeObjects), Is.True);
    }

    [Test]
    public async Task ASkippedTableIsDiscardedAndCannotBeReadLater()
    {
        var root = await OpenTreeAsync(LoadDump(RecordedDumps[0]), 0, string.Empty);
        var tables = await ChildrenAsync((await ChildrenAsync(root))[1]);

        // Reading the last table first passes the ones before it
        var last = Rows(await ReadAllAsync(tables[^1]));
        Assert.That(last, Does.EndWith("\\.\n\n\n"));

        Assert.That(async () => await ReadAllAsync(tables[0]), Throws.InstanceOf<InvalidOperationException>(),
            "A table that was passed cannot be read");
        Assert.That(async () => await ReadAllAsync(tables[^1]), Throws.InstanceOf<IOException>(),
            "A table can only be read once");
    }

    [Test]
    public async Task ADumpThatEndsEarlyFailsTheTableBeingReadAndEveryLaterOne()
    {
        var dump = LoadDump(RecordedDumps[0]);
        var complete = await OpenTreeAsync(dump, 0, string.Empty);
        var largeObjectsLength = (await ReadAllAsync((await ChildrenAsync(complete))[2])).Length;

        // Cut the archive inside the table data, a little before the large objects start
        var cut = dump.AsSpan(0, dump.Length - largeObjectsLength - 300).ToArray();
        var root = await OpenTreeAsync(cut, 1, "pg_dump: error: connection to server was lost");
        var top = await ChildrenAsync(root);
        var entries = (await ChildrenAsync(top[1])).Append(top[2]).ToList();

        var completeEntries = 0;
        var failed = false;
        foreach (var entry in entries)
        {
            // Opening always works, and the read fails, so the backup sees a stream that failed
            using var stream = await entry.OpenRead(CancellationToken.None);
            Assert.That(stream, Is.InstanceOf<IStreamedSourceStream>());

            if (!failed)
            {
                try
                {
                    await stream.CopyToAsync(Stream.Null);
                    completeEntries++;
                    continue;
                }
                catch (IOException ex)
                {
                    Assert.That(ex.Message, Does.Contain("connection to server was lost"));
                    failed = true;
                    continue;
                }
            }

            // Every entry after the failure fails on its first read with the same reason
            var laterEx = Assert.ThrowsAsync<IOException>(async () => await stream.ReadAtLeastAsync(new byte[10], 1, false, CancellationToken.None));
            Assert.That(laterEx!.Message, Does.Contain("connection to server was lost"));
        }

        Assert.That(failed, Is.True, "The cut must fail a table");
        Assert.That(completeEntries, Is.GreaterThan(0), "The tables before the cut are complete");
        Assert.That(completeEntries, Is.LessThan(entries.Count - 1), "The large objects after the cut must fail too");
    }

    [Test]
    public async Task ADumpProgramThatFailsAtTheStartFailsTheSession()
    {
        var ex = Assert.ThrowsAsync<UserInformationException>(() =>
            PostgreSQLDumpSession.StartAsync(new RecordedOutput([], 1, "pg_dump: error: password authentication failed"), CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("password authentication failed"));
        await Task.CompletedTask;
    }

    private static byte[] Segment(byte[] rows, int blockSize)
    {
        var position = 0;
        using var stream = new PostgreSQLDumpStream((buffer, _) =>
        {
            var count = Math.Min(Math.Min(buffer.Length, 777), rows.Length - position);
            rows.AsSpan(position, count).CopyTo(buffer.Span);
            position += count;
            return ValueTask.FromResult(count);
        }, new PostgreSQLSegmenter(blockSize), null);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] MakeRows(int count, int seed)
    {
        var random = new Random(seed);
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
            sb.Append(i).Append('\t').Append("name ").Append(random.Next()).Append('\t').Append(new string('x', random.Next(20, 120))).Append('\n');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Test]
    public void SegmentsStartOnBlockBoundariesAndGiveTheRowsBack()
    {
        const int blockSize = 1024;
        var rows = MakeRows(5000, 1);
        var padded = Segment(rows, blockSize);

        var copy = padded.ToArray();
        var length = PostgreSQLSegmenter.RemovePadding(copy);
        Assert.That(copy.AsSpan(0, length).SequenceEqual(rows), Is.True, "Dropping the padding must give the rows back");

        // Every run of padding ends on a block boundary, and no segment is longer than the limit plus a row
        var segmentStart = 0;
        var segments = 0;
        for (var i = 0; i < padded.Length; i++)
        {
            if (padded[i] != PostgreSQLSegmenter.Padding)
                continue;

            var end = i;
            while (i < padded.Length && padded[i] == PostgreSQLSegmenter.Padding)
                i++;
            Assert.That(i % blockSize, Is.EqualTo(0), "Padding must run to a block boundary");
            Assert.That(end - segmentStart, Is.GreaterThanOrEqualTo(blockSize * PostgreSQLSegmenter.MinSegmentBlocks));
            Assert.That(end - segmentStart, Is.LessThan(blockSize * PostgreSQLSegmenter.MaxSegmentBlocks + 200));
            segmentStart = i;
            segments++;
        }

        Assert.That(segments, Is.GreaterThan(50));
        Assert.That(padded.Length, Is.LessThan(rows.Length * 2), "The padding must stay well below the size of the rows");
    }

    [Test]
    public void ARowAddedEarlyChangesFewBlocks()
    {
        const int blockSize = 1024;
        var rows = MakeRows(5000, 2);

        // Insert one row after the tenth row
        var at = 0;
        for (var i = 0; i < 10; i++)
            at = Array.IndexOf(rows, (byte)'\n', at) + 1;
        var extra = Encoding.UTF8.GetBytes("99999\tinserted\trow\n");
        var changed = rows.AsSpan(0, at).ToArray().Concat(extra).Concat(rows.AsSpan(at).ToArray()).ToArray();

        static HashSet<string> Blocks(byte[] data, int size)
        {
            var result = new HashSet<string>();
            for (var i = 0; i < data.Length; i += size)
                result.Add(Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(data.AsSpan(i, Math.Min(size, data.Length - i)))));
            return result;
        }

        var before = Blocks(Segment(rows, blockSize), blockSize);
        var after = Blocks(Segment(changed, blockSize), blockSize);
        var kept = after.Count(before.Contains) / (double)after.Count;
        Assert.That(kept, Is.GreaterThan(0.9), "Most blocks must be the same after one row is added near the start");

        // Without segments, every block after the new row differs
        var plainKept = Blocks(changed, blockSize).Count(Blocks(rows, blockSize).Contains) / (double)Blocks(changed, blockSize).Count;
        Assert.That(plainKept, Is.LessThan(0.1));
    }
}
