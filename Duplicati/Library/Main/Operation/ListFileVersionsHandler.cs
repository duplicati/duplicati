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

namespace Duplicati.Library.Main.Operation;

/// <summary>
/// Handler for listing file versions
/// </summary>
internal static class ListFileVersionsHandler
{
    /// <summary>
    /// Runs the list file versions operation
    /// </summary>
    /// <param name="options">The options to use</param>
    /// <param name="result">The result class</param>
    /// <param name="paths">The paths to list versions for</param>
    /// <param name="offset">The offset to start listing from</param>
    /// <param name="limit">The maximum number of results to return</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async static Task RunAsync(Options options, ListFileVersionsResults result, string[] paths, long offset, long limit)
    {
        if (!System.IO.File.Exists(options.Dbpath) || options.NoLocalDb)
            throw new UserInformationException("No local database found, this operation requires a local database", "NoLocalDatabase");

        await using var db =
            await Database.LocalListDatabase.CreateAsync(options.Dbpath, options.SqlitePageCache)
                .ConfigureAwait(false);
        long[]? filesetIds = null;
        if (!options.AllVersions)
        {
            filesetIds = await db
                .GetFilesetIDs(options.Time, options.Version)
                .ToArrayAsync()
                .ConfigureAwait(false);

            if (filesetIds.Length == 0)
                throw new UserInformationException("No filesets found", "NoFilesetsFound");
        }

        if (paths == null || paths.Length == 0)
            throw new UserInformationException("No path specified", "NoPathSpecified");

        paths = paths.Select(path => Util.AppendDirSeparator(path)).ToArray();
        result.FileVersions = await db
            .ListFileVersions(paths, filesetIds, offset, limit)
            .ConfigureAwait(false);
    }
}
