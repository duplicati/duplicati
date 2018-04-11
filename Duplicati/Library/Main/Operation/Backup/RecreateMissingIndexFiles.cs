//  Copyright (C) 2017, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Main.Operation.Common;

namespace Duplicati.Library.Main.Operation.Backup
{
    internal static class RecreateMissingIndexFiles
    {
        /// <summary>
        /// The tag used for log messages
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(RecreateMissingIndexFiles));

        public static Task Run(BackupDatabase database, Options options, BackupResults result, ITaskReader taskreader)
        {
            return AutomationExtensions.RunTask(new
            {
                UploadChannel = Channels.BackendRequest.ForWrite
            },

			async self =>
			{
				if (options.IndexfilePolicy != Options.IndexFileStrategy.None)
				{
				    foreach (var blockfile in await database.GetMissingIndexFilesAsync())
				    {
				        if (!await taskreader.ProgressAsync)
				            return;

                        Logging.Log.WriteInformationMessage(LOGTAG, "RecreateMissingIndexFile", "Re-creating missing index file for {0}", blockfile);
                        var w = await Common.IndexVolumeCreator.CreateIndexVolume(blockfile, options, database);

				        if (!await taskreader.ProgressAsync)
				            return;

				        await database.UpdateRemoteVolumeAsync(w.RemoteFilename, RemoteVolumeState.Uploading, -1, null);
				        await self.UploadChannel.WriteAsync(new IndexVolumeUploadRequest(w));
				    }
				}
			});

		}

	}
}
