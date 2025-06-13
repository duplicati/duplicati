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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation;

/// <summary>
/// Handler for listing filesets
/// </summary>
internal static class ListFilesetsHandler
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ListFilesetsHandler));

    /// <summary>
    /// Lists all filesets in the remote store or local database
    /// </summary>
    /// <param name="options">The options to use</param>
    /// <param name="result">The result class</param>
    /// <param name="backendManager">The backend manager to use for listing</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public static async Task RunAsync(Options options, ListFilesetResults result, IBackendManager backendManager)
    {
        //Use a speedy local query
        if (System.IO.File.Exists(options.Dbpath) && !options.NoLocalDb)
        {
            using var db = new Database.LocalListDatabase(options.Dbpath, options.SqlitePageCache);
            result.Filesets = db.ListFilesetsExtended().ToArray();
            return;
        }

        Logging.Log.WriteInformationMessage(LOGTAG, "NoLocalDatabase", "No local database, accessing remote store");

        var filteredList = ListFilesHandler.ParseAndFilterFilesets(await backendManager.ListAsync(result.TaskControl.ProgressToken).ConfigureAwait(false), options);
        if (filteredList.Count == 0)
            throw new UserInformationException("No filesets found on remote target", "EmptyRemoteFolder");

        result.EncryptedFiles = filteredList.Any(x => !string.IsNullOrWhiteSpace(x.Value.EncryptionModule));
        result.Filesets =
            filteredList.Select(x => new ListFilesetResultFileset(x.Key, x.Value.Time, null, null, null)).ToArray();
    }
}
