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
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.Builtin.PostgreSQL;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for the checks of a PostgreSQL database, with the answers of the server given by the test
/// </summary>
[Category("PostgreSQL")]
public class PostgreSQLWebModuleTests
{
    private const string Url = "postgresql://?postgresql-host=db.example.com&postgresql-port=6543&postgresql-database=app&postgresql-username=backup&postgresql-password=s3cret";

    private static Dictionary<string, string> HealthyDatabase() => new()
    {
        [PostgreSQLWebModule.SessionQuery] = "170004|17.4 (Debian 17.4-1)|t|f|f\n",
        [PostgreSQLWebModule.TableSummaryQuery] = "12|0\n",
        [PostgreSQLWebModule.UnreadableTablesQuery] = "",
        [PostgreSQLWebModule.LargeObjectSummaryQuery] = "0|0\n",
        [PostgreSQLWebModule.CountQuery] = "12|3221225473\n",
        [PostgreSQLWebModule.CreatePrivilegeQuery] = "t\n",
    };

    private static (PostgreSQLWebModule Module, List<string> Asked) Module(Dictionary<string, string> answers)
    {
        var asked = new List<string>();
        return (new PostgreSQLWebModule((options, sql, _) =>
        {
            asked.Add(sql);
            return answers.TryGetValue(sql, out var answer)
                ? Task.FromResult(answer)
                : throw new UserInformationException($"psql: error: connection to server at \"{options.Host}\" failed", "PostgreSQLToolFailed");
        }), asked);
    }

    private static async Task<Dictionary<string, PostgreSQLWebModule.PermissionStatus>> ChecksAsync(Dictionary<string, string> answers, string url, bool requireEncryption, bool forRestore)
    {
        var options = new Dictionary<string, string>
        {
            [PostgreSQLWebModule.OperationOption] = PostgreSQLWebModule.CheckPermissionsOperation,
            [PostgreSQLWebModule.UrlOption] = url,
        };
        if (requireEncryption)
            options[PostgreSQLWebModule.RequireEncryptionOption] = "true";
        if (forRestore)
            options[PostgreSQLWebModule.PurposeOption] = PostgreSQLWebModule.RestorePurpose;

        var result = await Module(answers).Module.Execute(options, CancellationToken.None);
        return JsonSerializer.Deserialize<List<PostgreSQLWebModule.PermissionStatus>>(result[PostgreSQLWebModule.PermissionsResultKey])!.ToDictionary(x => x.Name);
    }

    [Test]
    public void TheModuleIsRegisteredForTheSchemeOfTheSource()
    {
        var module = Duplicati.Library.Backend.WebModules.BuiltInWebModules.Single(x => x.Key == PostgreSQLOptions.Scheme);
        Assert.That(module, Is.InstanceOf<PostgreSQLWebModule>());
    }

    [Test]
    public async Task ADatabaseTheRoleCanBackUpPassesEveryCheck()
    {
        var checks = await ChecksAsync(HealthyDatabase(), Url, false, false);

        Assert.That(checks.Keys, Is.EqualTo(new[]
        {
            PostgreSQLWebModule.ConnectionCheck, PostgreSQLWebModule.TlsCheck, PostgreSQLWebModule.ServerVersionCheck, PostgreSQLWebModule.TableAccessCheck,
            PostgreSQLWebModule.RowLevelSecurityCheck, PostgreSQLWebModule.LargeObjectsCheck, PostgreSQLWebModule.SuperuserCheck,
        }));
        Assert.That(checks.Values.All(x => x.Enabled), Is.True);
        Assert.That(checks.Values.Any(x => x.RequiredForRestore), Is.False);
        Assert.That(checks[PostgreSQLWebModule.ConnectionCheck].Description, Does.Contain("app").And.Contain("db.example.com:6543").And.Contain("backup"));
        Assert.That(checks[PostgreSQLWebModule.ConnectionCheck].Description, Does.Not.Contain("s3cret"));
        Assert.That(checks[PostgreSQLWebModule.TableAccessCheck].RequiredForBackup, Is.True);
    }

    [Test]
    public async Task AConnectionThatIsNotEncryptedOnlyFailsTheCheckWhenEncryptionIsRequired()
    {
        var answers = HealthyDatabase();
        answers[PostgreSQLWebModule.SessionQuery] = "170004|17.4|f|f|f\n";

        // A mode that connects without TLS when the server does not offer it
        var url = Url + "&postgresql-sslmode=prefer";

        var reported = (await ChecksAsync(answers, url, false, false))[PostgreSQLWebModule.TlsCheck];
        Assert.That(reported.Enabled, Is.False);
        Assert.That(reported.RequiredForBackup, Is.False, "A database on the same machine is often reached without TLS");

        var required = (await ChecksAsync(answers, url, true, false))[PostgreSQLWebModule.TlsCheck];
        Assert.That(required.Enabled, Is.False);
        Assert.That(required.RequiredForBackup, Is.True);

        var forRestore = (await ChecksAsync(answers, url, true, true))[PostgreSQLWebModule.TlsCheck];
        Assert.That(forRestore.RequiredForRestore, Is.True);
        Assert.That(forRestore.RequiredForBackup, Is.False);
    }

    [TestCase("require")]
    [TestCase("verify-ca")]
    [TestCase("verify-full")]
    public async Task AConnectionThroughAPoolerIsEncryptedWhenTheTlsModeDoesNotConnectWithoutTls(string sslMode)
    {
        // The server sees the pooler's connection, which has no TLS; the client tools' connection has
        var answers = HealthyDatabase();
        answers[PostgreSQLWebModule.SessionQuery] = "170004|17.4|f|f|f\n";
        var url = Url + "&postgresql-sslmode=" + sslMode;

        Assert.That((await ChecksAsync(answers, url, true, false))[PostgreSQLWebModule.TlsCheck].Enabled, Is.True);
        Assert.That((await ChecksAsync(answers, url, true, true))[PostgreSQLWebModule.TlsCheck].Enabled, Is.True);
    }

    [Test]
    public async Task WhatTheRoleCannotReadFailsTheBackupCheck()
    {
        var answers = HealthyDatabase();
        answers[PostgreSQLWebModule.SessionQuery] = "130021|13.21|t|f|f\n";
        answers[PostgreSQLWebModule.TableSummaryQuery] = "12|3\n";
        answers[PostgreSQLWebModule.UnreadableTablesQuery] = "app.orders\napp.\"odd name\"\n";
        answers[PostgreSQLWebModule.LargeObjectSummaryQuery] = "5|2\n";
        answers[PostgreSQLWebModule.UnreadableLargeObjectsQuery] = "16401\n16402\n";

        var checks = await ChecksAsync(answers, Url + "&postgresql-include-large-objects=true", false, false);

        Assert.That(checks[PostgreSQLWebModule.ServerVersionCheck].Enabled, Is.False);
        Assert.That(checks[PostgreSQLWebModule.TableAccessCheck].Enabled, Is.False);
        Assert.That(checks[PostgreSQLWebModule.TableAccessCheck].Description, Does.Contain("app.orders, app.\"odd name\""));
        Assert.That(checks[PostgreSQLWebModule.RowLevelSecurityCheck].Enabled, Is.False);
        Assert.That(checks[PostgreSQLWebModule.LargeObjectsCheck].Enabled, Is.False);
        Assert.That(checks[PostgreSQLWebModule.LargeObjectsCheck].RequiredForBackup, Is.True);
        Assert.That(checks[PostgreSQLWebModule.LargeObjectsCheck].Description, Does.Contain("16401, 16402"));
    }

    [Test]
    public async Task LargeObjectsThatAreLeftOutAndASuperuserAreReportedWithoutFailing()
    {
        var answers = HealthyDatabase();
        answers[PostgreSQLWebModule.SessionQuery] = "170004|17.4|t|t|f\n";
        answers[PostgreSQLWebModule.LargeObjectSummaryQuery] = "5|0\n";

        var checks = await ChecksAsync(answers, Url, false, false);

        Assert.That(checks[PostgreSQLWebModule.LargeObjectsCheck].Enabled, Is.False);
        Assert.That(checks[PostgreSQLWebModule.LargeObjectsCheck].RequiredForBackup, Is.False);
        Assert.That(checks[PostgreSQLWebModule.SuperuserCheck].Enabled, Is.False);
        Assert.That(checks[PostgreSQLWebModule.SuperuserCheck].RequiredForBackup, Is.False);
    }

    [Test]
    public async Task ARestoreTargetIsCheckedForWhatARestoreNeeds()
    {
        var answers = HealthyDatabase();
        answers[PostgreSQLWebModule.TableSummaryQuery] = "7|0\n";
        answers[PostgreSQLWebModule.CreatePrivilegeQuery] = "f\n";
        var (module, asked) = Module(answers);

        var result = await module.Execute(new Dictionary<string, string>
        {
            [PostgreSQLWebModule.OperationOption] = PostgreSQLWebModule.CheckPermissionsOperation,
            [PostgreSQLWebModule.UrlOption] = Url,
            [PostgreSQLWebModule.PurposeOption] = PostgreSQLWebModule.RestorePurpose,
        }, CancellationToken.None);
        var checks = JsonSerializer.Deserialize<List<PostgreSQLWebModule.PermissionStatus>>(result[PostgreSQLWebModule.PermissionsResultKey])!.ToDictionary(x => x.Name);

        Assert.That(checks.Keys, Is.EqualTo(new[]
        {
            PostgreSQLWebModule.ConnectionCheck, PostgreSQLWebModule.TlsCheck, PostgreSQLWebModule.ServerVersionCheck,
            PostgreSQLWebModule.CreateObjectsCheck, PostgreSQLWebModule.ExistingTablesCheck,
        }));
        Assert.That(checks[PostgreSQLWebModule.ConnectionCheck].RequiredForRestore, Is.True);
        Assert.That(checks.Values.Any(x => x.RequiredForBackup), Is.False);

        // What depends on what is restored is reported, and does not fail the check
        Assert.That(checks[PostgreSQLWebModule.CreateObjectsCheck].Enabled, Is.False);
        Assert.That(checks[PostgreSQLWebModule.CreateObjectsCheck].RequiredForRestore, Is.False);
        Assert.That(checks[PostgreSQLWebModule.ExistingTablesCheck].Enabled, Is.False);
        Assert.That(checks[PostgreSQLWebModule.ExistingTablesCheck].Description, Does.Contain("7 table(s)"));

        // The checks of a backup, such as reading every table, are not run against a restore target
        Assert.That(asked, Has.No.Member(PostgreSQLWebModule.UnreadableTablesQuery));
    }

    [Test]
    public async Task TheCountsAreTheTablesAndTheSizeTheServerReports()
    {
        var result = await Module(HealthyDatabase()).Module.Execute(new Dictionary<string, string>
        {
            [PostgreSQLWebModule.OperationOption] = PostgreSQLWebModule.CountItemsOperation,
            [PostgreSQLWebModule.UrlOption] = Url,
        }, CancellationToken.None);

        using var counts = JsonDocument.Parse(result[PostgreSQLWebModule.CountsResultKey]);
        Assert.That(counts.RootElement.GetProperty("tables").GetProperty("total").GetInt64(), Is.EqualTo(12));
        Assert.That(counts.RootElement.GetProperty("database").GetProperty("sizeBytes").GetInt64(), Is.EqualTo(3221225473));
    }

    [Test]
    public void AConnectionThatFailsIsReportedWithWhatTheClientWrote()
    {
        var (module, _) = Module([]);

        var ex = Assert.ThrowsAsync<UserInformationException>(() => module.Execute(new Dictionary<string, string>
        {
            [PostgreSQLWebModule.OperationOption] = PostgreSQLWebModule.CheckPermissionsOperation,
            [PostgreSQLWebModule.UrlOption] = Url,
        }, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("db.example.com"));
    }

    [Test]
    public void AnythingButADatabaseUrlAndAKnownOperationIsRefused()
    {
        var (module, asked) = Module(HealthyDatabase());

        Assert.ThrowsAsync<UserInformationException>(() => module.Execute(new Dictionary<string, string> { [PostgreSQLWebModule.OperationOption] = PostgreSQLWebModule.CountItemsOperation }, CancellationToken.None));
        Assert.ThrowsAsync<UserInformationException>(() => module.Execute(new Dictionary<string, string>
        {
            [PostgreSQLWebModule.OperationOption] = PostgreSQLWebModule.CountItemsOperation,
            [PostgreSQLWebModule.UrlOption] = "office365://?postgresql-host=h&postgresql-database=d&postgresql-username=u",
        }, CancellationToken.None));
        Assert.ThrowsAsync<UserInformationException>(() => module.Execute(new Dictionary<string, string>
        {
            [PostgreSQLWebModule.OperationOption] = "DropEverything",
            [PostgreSQLWebModule.UrlOption] = Url,
        }, CancellationToken.None));
        Assert.That(asked, Is.Empty);
    }
}
