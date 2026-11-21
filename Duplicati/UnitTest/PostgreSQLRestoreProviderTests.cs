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

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for the PostgreSQL restore provider that need no server: how the entries of a backup
/// are told apart, the statements and program arguments a restore uses, and how the rows of a
/// table are passed on. They use the recorded dumps of the source provider tests.
/// </summary>
[Category("PostgreSQL")]
public class PostgreSQLRestoreProviderTests
{
    private static readonly string[] RecordedDumps = ["pg14.dump", "pg15.dump", "pg16.dump", "pg17.dump", "pg18.dump", "pg19.dump"];

    private const string Url = "postgresql://?postgresql-host=db.example.com&postgresql-database=app&postgresql-username=restore&postgresql-password=s3cret";

    private static async Task<PostgreSQLArchiveReader> OpenDumpAsync(string name)
    {
        var assembly = typeof(PostgreSQLRestoreProviderTests).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith(name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        return await PostgreSQLArchiveReader.OpenAsync(ms, CancellationToken.None);
    }

    [Test]
    public void TheProviderIsRegisteredAndDescribesItsOptions()
    {
        var module = Duplicati.Library.SourceProviders.RestoreDestinationProviderModules.BuiltInRestoreDestinationProviderModules
            .Single(x => x.Key == PostgreSQLOptions.Scheme);

        var names = module.SupportedCommands.Select(x => x.Name).ToList();
        Assert.That(names, Has.Member(PostgreSQLOptions.HostOption));
        Assert.That(names, Has.Member(PostgreSQLRestoreOptions.ModeOption));
        Assert.That(names, Has.Member(PostgreSQLRestoreOptions.CreateTablesOption));
        Assert.That(names, Has.Member(PostgreSQLRestoreOptions.AllowMixedVersionsOption));
        Assert.That(names, Has.Member(PostgreSQLRestoreOptions.AllowMissingTablesOption));
        // A restore to the database of the source is given the options of the source, which must not be reported as unknown
        Assert.That(names, Is.SupersetOf(PostgreSQLOptions.SupportedCommands.Select(x => x.Name)));
        Assert.That(names, Is.Unique);
    }

    [Test]
    public void TheOptionsAreReadFromTheUrlAndTheRestoreOptions()
    {
        var defaults = PostgreSQLTestOptions.ParseRestore(Url, new Dictionary<string, string?>());
        Assert.That(defaults.TablesOnly, Is.False);
        Assert.That(defaults.Overwrite, Is.False);
        Assert.That(defaults.CreateTables, Is.False);
        Assert.That(defaults.AllowMixedVersions, Is.False);
        Assert.That(defaults.AllowMissingTables, Is.False);
        Assert.That(defaults.DryRun, Is.False);
        Assert.That(PostgreSQLTestOptions.ParseRestore(Url, new Dictionary<string, string?> { [PostgreSQLRestoreOptions.DryRunOption] = "true" }).DryRun, Is.True);
        Assert.That(PostgreSQLTestOptions.ParseRestore(Url, new Dictionary<string, string?> { [PostgreSQLRestoreOptions.DryRunAliasOption] = "true" }).DryRun, Is.True);
        Assert.That(defaults.Connection.Database, Is.EqualTo("app"));

        var options = PostgreSQLTestOptions.ParseRestore(Url + "&postgresql-restore-mode=tables", new Dictionary<string, string?>
        {
            [PostgreSQLRestoreOptions.ModeOption] = PostgreSQLRestoreOptions.ModeDatabase,
            [PostgreSQLRestoreOptions.OverwriteOption] = "true",
            [PostgreSQLRestoreOptions.CreateTablesOption] = null,
            [PostgreSQLRestoreOptions.AllowMixedVersionsOption] = "true",
        });
        Assert.That(options.TablesOnly, Is.True, "The URL wins over the restore options");
        Assert.That(options.Overwrite, Is.True);
        Assert.That(options.CreateTables, Is.True, "An option without a value is switched on");
        Assert.That(options.AllowMixedVersions, Is.True);

        Assert.That(() => PostgreSQLTestOptions.ParseRestore(Url + "&postgresql-restore-mode=everything", new Dictionary<string, string?>()),
            Throws.InstanceOf<UserInformationException>());

        // The legacy engine writes an entry once for each volume, at the offsets of its blocks
        Assert.That(() => PostgreSQLTestOptions.ParseRestore(Url, new Dictionary<string, string?> { [PostgreSQLRestoreOptions.RestoreLegacyOption] = "true" }),
            Throws.InstanceOf<UserInformationException>());
    }

    [Test]
    public async Task AWholeDatabaseRestoreNeedsTheTableOfContentsInTheSelection()
    {
        using var database = new PostgreSQLRestoreProvider(Url, PostgreSQLTestOptions.FromUrl(Url, new Dictionary<string, string?>()));
        Assert.That(() => database.SetRestoreSelection(["/postgresql/tables/app.orders"], CancellationToken.None), Throws.InstanceOf<UserInformationException>());
        await database.SetRestoreSelection(["/postgresql/toc", "/postgresql/tables/app.orders", "/postgresql/tables/app.orders" + StreamedSource.PartialEntrySuffix], CancellationToken.None);

        using var tables = new PostgreSQLRestoreProvider(Url, PostgreSQLTestOptions.FromUrl(Url + "&postgresql-restore-mode=tables", new Dictionary<string, string?>()));
        await tables.SetRestoreSelection(["/postgresql/tables/app.orders"], CancellationToken.None);
    }

    [TestCaseSource(nameof(RecordedDumps))]
    public async Task AWholeDatabaseRestoreNeedsEveryTableAndTheLargeObjects(string name)
    {
        var archive = await OpenDumpAsync(name);
        var required = PostgreSQLRestoreProvider.RequiredEntryNames(archive);

        Assert.That(required, Has.Member(PostgreSQLSourceProvider.TableEntryName("app", "orders")));
        Assert.That(required, Has.Member(PostgreSQLSourceProvider.LargeObjectsName));
        Assert.That(required, Has.No.Member(PostgreSQLSourceProvider.TableOfContentsName));
    }

    [Test]
    public void CommentsAreRestoredInTheSectionOfTheEntryBeforeThem()
    {
        PostgreSQLArchiveEntry Entry(int id, string description, int section)
            => new(id, false, null, "t" + id, description, "app", null, section, null, null);

        var entries = new[]
        {
            Entry(1, "TABLE", PostgreSQLArchiveReader.SectionPreData),
            Entry(2, "COMMENT", PostgreSQLArchiveReader.SectionNone),
            Entry(3, "TABLE DATA", PostgreSQLArchiveReader.SectionData),
            Entry(4, "INDEX", PostgreSQLArchiveReader.SectionPostData),
            Entry(5, "COMMENT", PostgreSQLArchiveReader.SectionNone),
        };

        var sections = PostgreSQLRestoreProvider.EffectiveSections(entries).ToDictionary(x => x.Entry.DumpId, x => x.Section);
        Assert.That(sections[2], Is.EqualTo(PostgreSQLArchiveReader.SectionPreData));
        Assert.That(sections[5], Is.EqualTo(PostgreSQLArchiveReader.SectionPostData));
    }

    [Test]
    public void TheProviderWritesEveryEntryFromItsStart()
    {
        using var provider = new PostgreSQLRestoreProvider(Url, PostgreSQLTestOptions.FromUrl(Url, new Dictionary<string, string?>()));

        Assert.That(provider.FileExists("/postgresql/tables/app.orders", CancellationToken.None).Result, Is.False);
        Assert.That(provider.TargetDestination, Is.Not.Empty);
        Assert.That(provider.GetPriorityFiles(), Is.EqualTo(new[] { Path.DirectorySeparatorChar + PostgreSQLSourceProvider.TableOfContentsName }));
        // A table whose name ends like the table of contents entry is not restored first
        Assert.That(provider.TargetDestination + Path.DirectorySeparatorChar + PostgreSQLSourceProvider.TableEntryName("app", "toc"),
            Does.Not.EndWith(provider.GetPriorityFiles()[0]));
    }

    [Test]
    public void EntriesAreToldApartByName()
    {
        Assert.That(PostgreSQLRestoreProvider.Classify("/postgresql/toc").Kind, Is.EqualTo(PostgreSQLRestoreProvider.EntryKind.TableOfContents));
        Assert.That(PostgreSQLRestoreProvider.Classify("/postgresql/large-objects").Kind, Is.EqualTo(PostgreSQLRestoreProvider.EntryKind.LargeObjects));
        Assert.That(PostgreSQLRestoreProvider.Classify("/postgresql/tables/app.orders" + StreamedSource.PartialEntrySuffix).Kind, Is.EqualTo(PostgreSQLRestoreProvider.EntryKind.Partial));
        Assert.That(PostgreSQLRestoreProvider.Classify("/postgresql/large-objects" + StreamedSource.PartialEntrySuffix).Kind, Is.EqualTo(PostgreSQLRestoreProvider.EntryKind.Partial));
        // A table can be named like the suffix, and is still a table
        Assert.That(PostgreSQLRestoreProvider.Classify("/postgresql/tables/" + PostgreSQLSourceProvider.TableEntryName("public", "duplicati-partial")),
            Is.EqualTo((PostgreSQLRestoreProvider.EntryKind.Table, "public.duplicati-partial", "public", "duplicati-partial")));

        var table = PostgreSQLRestoreProvider.Classify("/postgresql/tables/app.orders");
        Assert.That(table, Is.EqualTo((PostgreSQLRestoreProvider.EntryKind.Table, "app.orders", "app", "orders")));

        // The folders are not needed, as a restore of one table has none, and a Windows path works too
        Assert.That(PostgreSQLRestoreProvider.Classify("\\postgresql\\app.orders").Table, Is.EqualTo("orders"));

        var odd = PostgreSQLRestoreProvider.Classify("/postgresql/tables/" + PostgreSQLSourceProvider.TableEntryName("my.schema", "odd.name/x"));
        Assert.That(odd.Schema, Is.EqualTo("my.schema"));
        Assert.That(odd.Table, Is.EqualTo("odd.name/x"));

        Assert.That(() => PostgreSQLRestoreProvider.Classify("/postgresql/readme"), Throws.InstanceOf<UserInformationException>());
    }

    [Test]
    public void NamesAndTextsAreQuoted()
    {
        Assert.That(PostgreSQLRestoreProvider.QuoteIdentifier("orders"), Is.EqualTo("\"orders\""));
        Assert.That(PostgreSQLRestoreProvider.QuoteIdentifier("a\"b"), Is.EqualTo("\"a\"\"b\""));
        Assert.That(PostgreSQLRestoreProvider.QualifiedName("App", "My Table"), Is.EqualTo("\"App\".\"My Table\""));
        Assert.That(PostgreSQLRestoreProvider.QuoteLiteral("it's"), Is.EqualTo("'it''s'"));
        Assert.That(PostgreSQLRestoreProvider.QuoteLiteral("a\\b'c"), Is.EqualTo("E'a\\\\b''c'"));
        Assert.That(PostgreSQLRestoreProvider.DatabaseConnectionString("my db's=x"), Is.EqualTo("dbname='my db\\'s=x'"));
    }

    [Test]
    public void TheProgramArgumentsNeverApplyOwnersOrGrantsAndStopAtTheFirstError()
    {
        var arguments = PostgreSQLRestoreProvider.BuildRestoreArguments("app", ["--section=post-data"]);
        Assert.That(arguments, Is.EqualTo(new[] { "--section=post-data", "--no-owner", "--no-acl", "--exit-on-error", "--no-password", "--dbname=dbname='app'" }));

        Assert.That(PostgreSQLRestoreProvider.BuildScriptArguments(true), Has.Member("--set=ON_ERROR_STOP=1"));
        Assert.That(PostgreSQLRestoreProvider.BuildScriptArguments(false), Has.No.Member("--set=ON_ERROR_STOP=1"));

        Assert.That(PostgreSQLRestoreProvider.ArchiveClientMajor("16.4 (Debian 16.4-1.pgdg120+2)"), Is.EqualTo(16));
        Assert.That(PostgreSQLRestoreProvider.ArchiveClientMajor("18beta1"), Is.EqualTo(18));
        Assert.That(PostgreSQLRestoreProvider.ArchiveClientMajor("unknown"), Is.Null);
    }

    [TestCaseSource(nameof(RecordedDumps))]
    public async Task TheRowsOfATableAreLoadedInATransactionWithTheSessionOfTheDump(string name)
    {
        var archive = await OpenDumpAsync(name);

        var script = PostgreSQLRestoreProvider.BuildLoadScript(archive, "app", "orders", false, false, "key1");
        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // The text from the backup can only be read as statements and rows, never as commands of psql
        Assert.That(lines[0], Is.EqualTo("\\restrict key1"));
        Assert.That(lines[1], Is.EqualTo("BEGIN;"));
        Assert.That(script, Does.Not.Contain("\\unrestrict"));
        Assert.That(lines, Has.Member("SET client_encoding = 'UTF8';"));
        Assert.That(lines, Has.Some.Contains("search_path"));
        Assert.That(lines[^1], Is.EqualTo("COPY app.orders (id, customer_id, total, created) FROM stdin;"));
        Assert.That(script, Does.EndWith("\n"));
        Assert.That(script, Does.Not.Contain("TRUNCATE").And.Not.Contain("RAISE"), "A table of a whole database restore was just created");

        // The name of the entry is matched exactly, with the dot it has in the database
        Assert.That(PostgreSQLRestoreProvider.BuildLoadScript(archive, "app", "odd.name", false, false, "key1"), Does.Contain("COPY app.\"odd.name\" (id, payload) FROM stdin;"));

        Assert.That(() => PostgreSQLRestoreProvider.BuildLoadScript(archive, "app", "missing", false, false, "key1"), Throws.InstanceOf<UserInformationException>());
    }

    [Test]
    public async Task RowsAreOnlyAddedToATableThatExistsWhenItIsEmptyOrMayBeReplaced()
    {
        var archive = await OpenDumpAsync(RecordedDumps[0]);

        var guarded = PostgreSQLRestoreProvider.BuildLoadScript(archive, "app", "orders", true, false, "key1");
        Assert.That(guarded, Does.Contain("IF EXISTS (SELECT 1 FROM \"app\".\"orders\") THEN RAISE EXCEPTION"));
        // No time limit of the role or database may end a COPY that waits for the restore to write the rows
        Assert.That(guarded, Does.Contain("BEGIN;\n" + PostgreSQLRestoreProvider.LoadSessionSettings));
        Assert.That(guarded, Does.Contain("SET statement_timeout = 0;").And.Contain("transaction_timeout"));
        // The quote of the body is one a table name cannot end
        Assert.That(guarded, Does.Contain("DO $dkey1$ BEGIN").And.Contain("END $dkey1$;"));
        Assert.That(guarded, Does.Not.Contain("TRUNCATE"));
        Assert.That(guarded.IndexOf("RAISE", StringComparison.Ordinal), Is.LessThan(guarded.IndexOf("COPY app.orders", StringComparison.Ordinal)));

        var replacing = PostgreSQLRestoreProvider.BuildLoadScript(archive, "app", "orders", true, true, "key1");
        Assert.That(replacing, Does.Contain("TRUNCATE TABLE ONLY \"app\".\"orders\";\nCOPY app.orders"));
        Assert.That(replacing, Does.Not.Contain("RAISE"));

        // Without the table of contents the table is named from the entry and takes its columns in its own order
        var bare = PostgreSQLRestoreProvider.BuildLoadScript(null, "App", "it's \"odd\"", true, true, "key1");
        Assert.That(bare, Does.StartWith("\\restrict key1\nBEGIN;\n" + PostgreSQLRestoreProvider.LoadSessionSettings + "TRUNCATE TABLE ONLY \"App\".\"it's \"\"odd\"\"\";\n"));
        Assert.That(bare, Does.EndWith("COPY \"App\".\"it's \"\"odd\"\"\" FROM stdin;\n"));
    }

    [TestCaseSource(nameof(RecordedDumps))]
    public async Task TheObjectsOfABackupAreDroppedLastCreatedFirst(string name)
    {
        var archive = await OpenDumpAsync(name);
        var script = PostgreSQLRestoreProvider.BuildDropScript(archive);

        int At(string text)
        {
            var index = script.IndexOf(text, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"The script must have: {text}");
            return index;
        }

        Assert.That(At("DROP CONSTRAINT orders_customer_id_fkey"), Is.LessThan(At("DROP TABLE app.orders;")));
        Assert.That(At("DROP INDEX app.orders_customer;"), Is.LessThan(At("DROP TABLE app.orders;")));
        At("DROP TABLE app.customers;");

        // A schema can hold objects that are not in the backup, so it is kept
        Assert.That(script, Does.Not.Contain("DROP SCHEMA"));

        // A dump names the database it was taken from, which must never be dropped: it can be
        // another database on the server that is restored to
        Assert.That(archive.Entries.Any(x => x.DropStatement != null && x.DropStatement.Contains("DROP DATABASE", StringComparison.Ordinal)), Is.True,
            "The recorded dumps have the entry this guards against");
        Assert.That(script, Does.Not.Contain("DATABASE"));

        // The large objects are removed by id, however the version records them
        Assert.That(script.Split('\n').Count(x => x.StartsWith("SELECT pg_catalog.lo_unlink('", StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(script, Does.Not.Contain("-- dummy"));
    }

    [TestCaseSource(nameof(RecordedDumps))]
    public async Task TheObjectsAreReplacedInOneTransaction(string name)
    {
        var archive = await OpenDumpAsync(name);
        const string created = "\\restrict theirs\nCREATE TABLE app.orders (id bigint);\n\\unrestrict theirs\n";

        var script = Encoding.UTF8.GetString(PostgreSQLRestoreProvider.BuildSchemaScript(archive, Encoding.UTF8.GetBytes(created), true, "mine"));
        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

        int Line(string text)
        {
            var index = lines.FindIndex(x => x.Contains(text, StringComparison.Ordinal));
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"The script must have: {text}");
            return index;
        }

        // The drops may fail one by one, and everything after them stops at the first error
        Assert.That(lines[0], Is.EqualTo("\\set ON_ERROR_ROLLBACK on"));
        Assert.That(Line("BEGIN;"), Is.LessThan(Line("DROP TABLE app.orders;")));
        Assert.That(Line("DROP TABLE app.orders;"), Is.LessThan(Line("\\set ON_ERROR_STOP on")));
        Assert.That(Line("\\set ON_ERROR_STOP on"), Is.LessThan(Line("CREATE SCHEMA IF NOT EXISTS \"app\";")));
        Assert.That(Line("CREATE SCHEMA IF NOT EXISTS \"app\";"), Is.LessThan(Line("CREATE TABLE app.orders (id bigint);")));
        Assert.That(lines[^1], Is.EqualTo("COMMIT;"));
        Assert.That(lines.Count(x => x == "BEGIN;"), Is.EqualTo(1));

        // The statements from the backup are between a restrict and its unrestrict, so they cannot run commands of psql
        var restricted = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("\\restrict ", StringComparison.Ordinal))
                restricted = true;
            else if (line.StartsWith("\\unrestrict ", StringComparison.Ordinal))
                restricted = false;
            else if (line.StartsWith("DROP ", StringComparison.Ordinal) || line.StartsWith("ALTER ", StringComparison.Ordinal) || line.StartsWith("SELECT pg_catalog.lo_unlink", StringComparison.Ordinal) || line.StartsWith("CREATE ", StringComparison.Ordinal))
                Assert.That(restricted, Is.True, $"Not restricted: {line}");
            else if (line.StartsWith("\\set ", StringComparison.Ordinal))
                Assert.That(restricted, Is.False, $"A setting of psql cannot be changed while restricted: {line}");
        }

        // Without replacing, nothing is dropped
        var creating = Encoding.UTF8.GetString(PostgreSQLRestoreProvider.BuildSchemaScript(archive, Encoding.UTF8.GetBytes(created), false, "mine"));

        // What pg_restore wrote passes unchanged, even when it is not valid UTF-8
        var raw = new byte[] { (byte)'\\', (byte)'r', 0xE9, (byte)'\n' };
        Assert.That(PostgreSQLRestoreProvider.BuildSchemaScript(archive, raw, false, "mine").AsSpan().IndexOf(raw.AsSpan(0, 3)), Is.GreaterThan(0));
        Assert.That(PostgreSQLRestoreProvider.IsRestricted(Encoding.UTF8.GetBytes(created)), Is.True);
        Assert.That(PostgreSQLRestoreProvider.IsRestricted("CREATE TABLE t (id int);\n"u8), Is.False);
        Assert.That(creating, Does.Not.Contain("DROP ").And.Not.Contain("lo_unlink"));
        Assert.That(creating, Does.Contain("CREATE SCHEMA IF NOT EXISTS \"app\";"));

        // The schemas and the database are not among the objects pg_restore creates
        var ids = PostgreSQLRestoreProvider.ObjectEntryIds(archive);
        var listed = archive.Entries.Where(x => ids.Contains(x.DumpId)).ToList();
        Assert.That(listed.Select(x => x.Description), Has.Member("TABLE").And.No.Member("SCHEMA").And.No.Member("DATABASE").And.No.Member("TABLE DATA").And.No.Member("INDEX"));
    }

    [TestCaseSource(nameof(RecordedDumps))]
    public async Task TheLargeObjectsAreRestoredFromTheirOwnEntries(string name)
    {
        var archive = await OpenDumpAsync(name);
        var content = archive.Entries.Where(x => x.HasData && x.Description == PostgreSQLArchiveReader.LargeObjectsDescription).Select(x => x.DumpId).ToList();
        Assert.That(content, Is.Not.Empty);

        var everything = PostgreSQLRestoreProvider.LargeObjectEntryIds(archive, false);
        var afterObjects = PostgreSQLRestoreProvider.LargeObjectEntryIds(archive, true);

        Assert.That(everything, Is.SupersetOf(content));
        Assert.That(afterObjects, Is.SupersetOf(content));
        Assert.That(everything.Count, Is.GreaterThan(content.Count), "Restored on their own, the objects are created as well");

        // What a whole database restore already created with the other objects is not created again
        var created = archive.Entries.Where(x => everything.Contains(x.DumpId) && x.Section == PostgreSQLArchiveReader.SectionPreData).Select(x => x.DumpId).ToList();
        Assert.That(afterObjects, Is.EquivalentTo(everything.Except(created)));
    }

    private static byte[] Run(PostgreSQLRowFilter filter, byte[] stored, int chunk)
    {
        using var result = new MemoryStream();
        for (var offset = 0; offset < stored.Length; offset += chunk)
        {
            var part = stored.AsSpan(offset, Math.Min(chunk, stored.Length - offset));
            var output = new byte[part.Length];
            result.Write(output, 0, filter.Filter(part, output));
        }

        return result.ToArray();
    }

    [Test]
    public void TheRowsArePassedOnWithoutPaddingUpToTheirEndMarker()
    {
        // Rows as COPY text: a value that starts with a backslash and a dot has its backslash doubled
        const string rows = "1\tfirst\n2\t\\\\.not the end\n\\\\.\tneither\n3\tlast\\\\.\n";
        const string end = "\\.\n";
        var stored = Encoding.UTF8.GetBytes(rows.Substring(0, 9) + "\0\0\0\0\0" + rows.Substring(9) + end + "\n\n" + "\0\0\0\0\0\0\0");

        foreach (var chunk in new[] { 1, 2, 3, 7, stored.Length })
        {
            var filter = new PostgreSQLRowFilter("app.t");
            var passed = Run(filter, stored, chunk);

            Assert.That(Encoding.UTF8.GetString(passed), Is.EqualTo(rows + end), $"In pieces of {chunk}");
            Assert.That(filter.EndSeen, Is.True);
        }
    }

    [Test]
    public void AnEmptyTableIsOnlyItsEndMarker()
    {
        var filter = new PostgreSQLRowFilter("app.t");
        var passed = Run(filter, Encoding.UTF8.GetBytes("\\.\n\n\n\0\0"), 2);

        Assert.That(Encoding.UTF8.GetString(passed), Is.EqualTo("\\.\n"));
        Assert.That(filter.EndSeen, Is.True);
    }

    [Test]
    public void RowsThatAreCutShortHaveNoEnd()
    {
        var filter = new PostgreSQLRowFilter("app.t");
        var passed = Run(filter, Encoding.UTF8.GetBytes("1\tfirst\n2\tsec\0\0\0"), 4);

        Assert.That(Encoding.UTF8.GetString(passed), Is.EqualTo("1\tfirst\n2\tsec"));
        Assert.That(filter.EndSeen, Is.False, "Without the end marker the rows are not committed");
    }

    [Test]
    public void NothingAfterTheEndMarkerIsPassedOn()
    {
        var filter = new PostgreSQLRowFilter("app.t");
        var output = new byte[64];

        // What follows the marker would be read as statements, so it is refused
        Assert.That(() => filter.Filter(Encoding.UTF8.GetBytes("1\tfirst\n\\.\nDROP TABLE app.t;\n"), output), Throws.InstanceOf<IOException>());
    }

    [Test]
    public async Task TheTableOfContentsIsKeptAndThePartOfAFailedDumpIsDropped()
    {
        using var provider = new PostgreSQLRestoreProvider(Url, PostgreSQLTestOptions.FromUrl(Url, new Dictionary<string, string?>()));

        await using (var toc = await provider.OpenReadWrite("/postgresql/toc", CancellationToken.None))
        {
            await toc.WriteAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);
            Assert.That(toc.Length, Is.EqualTo(3), "The length is what was written, so the restore engine does not cut the entry");
            toc.SetLength(3);
        }

        await using var partial = await provider.OpenWrite("/postgresql/tables/app.orders" + StreamedSource.PartialEntrySuffix, CancellationToken.None);
        await partial.WriteAsync(new byte[100], CancellationToken.None);
        Assert.That(partial.Length, Is.EqualTo(100));

        Assert.That(() => provider.OpenRead("/postgresql/toc", CancellationToken.None), Throws.InstanceOf<NotSupportedException>());
    }
}
