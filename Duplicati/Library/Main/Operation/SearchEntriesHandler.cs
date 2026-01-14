// Copyright (C) 2025, The Duplicati Team
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

using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation;

/// <summary>
/// Handler for searching entries in a backup fileset
/// </summary>
internal static class SearchEntriesHandler
{
    /// <summary>
    /// Runs the search entries operation
    /// </summary>
    /// <param name="options">The options to use</param>
    /// <param name="result">The result class</param>
    /// <param name="paths">The paths to search</param>
    /// <param name="filter">The filter to use for searching</param>
    /// <param name="offset">The offset to start searching from</param>
    /// <param name="limit">The maximum number of results to return</param>
    /// <param name="extendedData">Whether to include extended data</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public static async Task RunAsync(Options options, SearchFilesResults result, string[] paths, IFilter filter, long offset, long limit, bool extendedData)
    {
        if (!System.IO.File.Exists(options.Dbpath))
            throw new UserInformationException("No local database found, this operation requires a local database", "NoLocalDatabase");

        await using var db =
            await Database.LocalListDatabase.CreateAsync(options.Dbpath, null, result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
        long[]? filesetIds = null;
        if (!options.AllVersions)
        {
            filesetIds = await db
                .GetFilesetIDs(options.Time, options.Version, false, result.TaskControl.ProgressToken)
                .ToArrayAsync(cancellationToken: result.TaskControl.ProgressToken)
                .ConfigureAwait(false);

            if (filesetIds.Length == 0)
                throw new UserInformationException("No filesets found", "NoFilesetsFound");
        }

        if (paths != null)
            paths = paths.Select(path => Util.AppendDirSeparator(path)).ToArray();

        result.FileVersions = await db
            .SearchEntries(paths, filter, filesetIds, offset, limit, result.TaskControl.ProgressToken)
            .ConfigureAwait(false);

        if (extendedData)
        {
            var coreentries = result.FileVersions.Items.Cast<SearchFileVersion>().ToArray();
            var metadata = await db.GetMetadataForFilesetIds(coreentries.Select(e => e.FileId), result.TaskControl.ProgressToken).ConfigureAwait(false);

            for (int i = 0; i < coreentries.Length; i++)
            {
                if (metadata.TryGetValue(coreentries[i].FileId, out var dict))
                    coreentries[i] = coreentries[i] with { Metadata = dict };
            }

            result.FileVersions = new PaginatedResults<ISearchFileVersion>(result.FileVersions.Page, result.FileVersions.PageSize, result.FileVersions.TotalPages, result.FileVersions.TotalCount, coreentries);
        }
    }
}
