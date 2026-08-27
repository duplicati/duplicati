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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main.Database.Local;

namespace Duplicati.Library.Main.Operation
{
    /// <summary>
    /// Handler for updating the label of a backup version.
    /// The label is stored in the local database and will be included
    /// in the labels.json file of the next backup; no new filelist volume
    /// is written just to update a label.
    /// </summary>
    internal static class SetVersionLabelHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Log.LogTagFromType(typeof(SetVersionLabelHandler));

        /// <summary>
        /// Updates the label of a backup version in the local database.
        /// </summary>
        /// <param name="options">The options to use</param>
        /// <param name="result">The result class</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public static async Task RunAsync(Options options, SetVersionLabelResults result)
        {
            var token = result.TaskControl.ProgressToken;

            if (options.Version == null || options.Version.Length != 1)
                throw new UserInformationException("You must specify exactly one version to update the label for", "NoVersionSpecifiedForLabelUpdate");

            if (string.IsNullOrWhiteSpace(options.Dbpath) || !File.Exists(options.Dbpath))
                throw new UserInformationException("The local database does not exist, the label can only be updated when a local database is present", "LocalDatabaseMissing");

            var version = options.Version[0];
            var label = options.VersionName;

            await using var db = await LocalListDatabase.CreateAsync(options.Dbpath, null, token)
                .ConfigureAwait(false);

            var filesets = await db
                .FilesetTimesAsync(token)
                .ToArrayAsync(cancellationToken: token)
                .ConfigureAwait(false);

            if (version < 0 || version >= filesets.Length)
                throw new UserInformationException($"No such version: {version}", "VersionNotFoundForLabelUpdate");

            var fileset = filesets[version];

            Log.WriteInformationMessage(LOGTAG, "UpdatingVersionLabel", "Setting label for version {0} ({1:u}) to \"{2}\"", version, fileset.Value.ToUniversalTime(), label);

            await db.UpdateFilesetLabelAsync(fileset.Key, label, token).ConfigureAwait(false);
            await db.Transaction.CommitAsync(token).ConfigureAwait(false);

            result.BackupVersion = version;
            result.Time = fileset.Value;
            result.Label = label;
            result.EndTime = DateTime.UtcNow;
        }
    }
}
