#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Main.LiveControl
{
    /// <summary>
    /// An event that is invoked when the backup state changes
    /// </summary>
    public interface ILiveControl
    {
        /// <summary>
        /// Activates an asyncronous request for pausing the backup
        /// </summary>
        void Pause();

        /// <summary>
        /// Activates an asyncronous request for resuming a paused backup
        /// </summary>
        void Resume();

        /// <summary>
        /// Activates an asyncronous request for stopping the backup.
        /// Stopping a backup does not interrupt ongoing uploads.
        /// </summary>
        void Stop();

        /// <summary>
        /// Activates an asyncronous request for terminating the backup.
        /// Terminating a backup breaks any ongoing uploads, and may leave partial files on the backend.
        /// </summary>
        void Terminate();

        /// <summary>
        /// Gets a value indicating if stop has been requested
        /// </summary>
        bool IsStopRequested { get; }

        /// <summary>
        /// Sets the upload limit in bytes pr. second.
        /// Setting this value does not permit higher speeds than the backup dictates.
        /// Set to zero to use the backup defaults.
        /// </summary>
        /// <param name="limit">The limit, kan use kb/mb/gb suffix</param>
        void SetUploadLimit(string limit);

        /// <summary>
        /// Sets the download limit in bytes pr. second.
        /// Setting this value does not permit higher speeds than the backup dictates.
        /// Set to zero to use the backup defaults.
        /// </summary>
        /// <param name="limit">The limit, kan use kb/mb/gb suffix</param>
        void SetDownloadLimit(string limit);

        /// <summary>
        /// Forces the thread priority to something different than the backup specifies.
        /// </summary>
        /// <param name="priority">The new priority to use</param>
        void SetThreadPriority(System.Threading.ThreadPriority priority);

        /// <summary>
        /// Clears an overrided thread priority.
        /// </summary>
        void UnsetThreadPriority();
    }
}
