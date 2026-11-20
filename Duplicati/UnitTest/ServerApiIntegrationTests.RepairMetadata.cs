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
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Endpoints.V1.Backup;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The number of versions the server shows for a job after a repair
/// </summary>
public partial class ServerApiIntegrationTests
{
    /// <summary>
    /// A job imported from an older export carries the number of versions of that time. Recreating its
    /// database from the destination should show the versions that are there, not wait for the next backup (#3114).
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task ARecreateShowsTheVersionsAtTheDestination_Async()
    {
        const string passphrase = "repair-metadata-passphrase";

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var firstId = await CreateBackupAsync(httpClient, passphrase).ConfigureAwait(false);
            for (var i = 0; i < 3; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(DATAFOLDER, $"file{i}.txt"), $"version {i}").ConfigureAwait(false);
                await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{firstId}/run").ConfigureAwait(false);
            }
            Assert.That((await MetadataAsync(httpClient, firstId).ConfigureAwait(false)).GetValueOrDefault("BackupListCount"), Is.EqualTo("3"));

            // The same destination in a job with the metadata of an export made after the first version, and no local database yet
            var stale = new Dictionary<string, string> { ["BackupListCount"] = "1", ["TargetFilesetsCount"] = "1" };
            var importedId = await CreateBackupAsync(httpClient, passphrase, metadata: stale).ConfigureAwait(false);
            Assert.That((await MetadataAsync(httpClient, importedId).ConfigureAwait(false)).GetValueOrDefault("BackupListCount"), Is.EqualTo("1"));

            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{importedId}/repair").ConfigureAwait(false);

            var metadata = await MetadataAsync(httpClient, importedId).ConfigureAwait(false);
            Assert.That(metadata.GetValueOrDefault("BackupListCount"), Is.EqualTo("3"), "The recreate should show the versions at the destination");
            Assert.That(metadata.GetValueOrDefault("TargetFilesetsCount"), Is.EqualTo("3"));
        }).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, string>> MetadataAsync(HttpClient httpClient, string backupId)
    {
        var stored = await httpClient.GetFromJsonAsync<BackupGet.GetBackupResultDto>($"/api/v1/backup/{backupId}", JsonOptions).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Backup get response was empty");
        return new Dictionary<string, string>(stored.Backup.Metadata ?? new Dictionary<string, string>());
    }
}
