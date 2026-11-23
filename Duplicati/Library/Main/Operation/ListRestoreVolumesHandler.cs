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

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation
{
    /// <summary>
    /// Works out which remote volumes a restore of the selected files reads, from the local
    /// database and without contacting the destination. Files kept in archive storage, such as
    /// Amazon S3 Glacier, cannot be read until they are brought back, which can take hours, so
    /// the list tells what to bring back before running the restore (#6228).
    /// </summary>
    internal class ListRestoreVolumesHandler
    {
        /// <summary>
        /// The options for the operation
        /// </summary>
        private readonly Options m_options;
        /// <summary>
        /// The results of the operation
        /// </summary>
        private readonly ListRestoreVolumesResults m_result;

        /// <summary>
        /// Creates a new handler
        /// </summary>
        /// <param name="options">The options for the operation</param>
        /// <param name="result">The results of the operation</param>
        public ListRestoreVolumesHandler(Options options, ListRestoreVolumesResults result)
        {
            m_options = options;
            m_result = result;
        }

        /// <summary>
        /// Lists the remote volumes the restore of the selected files reads
        /// </summary>
        /// <param name="paths">The paths to restore, as given to the restore command</param>
        /// <param name="filter">The filter to restore with</param>
        /// <returns>An awaitable task</returns>
        public async Task RunAsync(string[]? paths, IFilter? filter)
        {
            if (!File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            // The same selection as the restore makes
            filter = JoinedFilterExpression.Join(new FilterExpression(paths), filter);

            var token = m_result.TaskControl.ProgressToken;
            await using var db = await LocalRestoreDatabase.CreateAsync(m_options.Dbpath, null, token).ConfigureAwait(false);

            var files = await db
                .PrepareRestoreFilelistAsync(m_options.Time, m_options.Version, filter, m_options.DisableAdsRestore, token)
                .ConfigureAwait(false);
            m_result.FileCount = files.Item1;
            m_result.FileSize = files.Item2;

            // Nothing is taken from local files here, so every block the files consist of is
            // read from the destination, which is the most a restore can need
            await db.FindMissingBlocksAsync(m_options.SkipMetadata, token).ConfigureAwait(false);
            m_result.Volumes = await db.GetMissingVolumesAsync(token)
                .Select(x => (IListRestoreVolume)new ListRestoreVolume(x.Name, x.Size))
                .ToListAsync(token)
                .ConfigureAwait(false);
        }
    }
}
