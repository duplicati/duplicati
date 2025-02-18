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

using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;

namespace Duplicati.Library.Main.Operation.Backup;

internal static class RecreateMissingIndexFiles
{
	/// <summary>
	/// The tag used for log messages
	/// </summary>
	private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(RecreateMissingIndexFiles));

	public static async Task Run(BackupDatabase database, IBackendManager backendManager, Options options, ITaskReader taskreader)
	{
		if (options.IndexfilePolicy != Options.IndexFileStrategy.None)
		{
			foreach (var blockfile in await database.GetMissingIndexFilesAsync().ConfigureAwait(false))
			{
				if (!await taskreader.ProgressRendevouz().ConfigureAwait(false))
					return;

				Logging.Log.WriteInformationMessage(LOGTAG, "RecreateMissingIndexFile", "Re-creating missing index file for {0}", blockfile);
				var w = await Common.IndexVolumeCreator.CreateIndexVolume(blockfile, options, database).ConfigureAwait(false);

				if (!await taskreader.ProgressRendevouz().ConfigureAwait(false))
					return;

				await database.UpdateRemoteVolumeAsync(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null).ConfigureAwait(false);
				await backendManager.PutAsync(w, null, null, false, taskreader.ProgressToken).ConfigureAwait(false);
			}
		}
	}

}

