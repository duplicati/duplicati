//  Copyright (C) 2018, The Duplicati Team
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

namespace Duplicati.Library.Main.Operation.Common
{
    public interface IBackendHandlerDatabase
    {
        /// <summary>
        /// Updates the remote volume information.
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="name">The name of the remote volume to update.</param>
        /// <param name="state">The new volume state.</param>
        /// <param name="size">The new volume size.</param>
        /// <param name="hash">The new volume hash.</param>
        /// <param name="suppressCleanup">If set to <c>true</c> suppress cleanup operation.</param>
        /// <param name="deleteGraceTime">The new delete grace time.</param>
        Task UpdateRemoteVolumeAsync(string name, RemoteVolumeState state, long size, string hash, bool suppressCleanup = false, TimeSpan deleteGraceTime = default(TimeSpan));

        /// <summary>
        /// Renames a remote file in the database
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="oldname">The old filename.</param>
        /// <param name="newname">The new filename.</param>
        Task RenameRemoteFileAsync(string oldname, string newname);

        /// <summary>
        /// Writes remote operation log data to the database
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="operation">The operation performed.</param>
        /// <param name="path">The remote path used.</param>
        /// <param name="data">Any data reported by the operation.</param>
        Task LogRemoteOperationAsync(string operation, string path, string data);

        /// <summary>
        /// Writes the current changes to the database
        /// </summary>
        /// <returns>An awaitable task.</returns>
        /// <param name="message">The message to use for logging the time spent in this operation.</param>
        /// <param name="restart">If set to <c>true</c>, a transaction will be started again after this call.</param>
        Task CommitTransactionAsync(string message, bool restart = true);
    }
}
