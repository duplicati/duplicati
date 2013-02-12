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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface for logging interactions with the backend
    /// </summary>
    public interface IBackendInteraction
    {
        /// <summary>
        /// Signals the start of a operation on the backend, such as cleanup or backup
        /// </summary>
        /// <param name='name'>The name of the operation</param>
        void BeginOperation(string name);
        
        /// <summary>
        /// Signals the end of the operation
        /// </summary>
        void EndOperation();
        
        /// <summary>
        /// Registers a get operation
        /// </summary>
        /// <param name='entry'>The entry involved</param>
        /// <param name='success'>True if the operation succeeeded, false otherwise</param>
        /// <param name='errorMessage'>An error message if the operation did not succeeed</param>
        void RegisterGet(IFileEntry entry, bool success, String errorMessage);

        /// <summary>
        /// Registers a put operation
        /// </summary>
        /// <param name='entry'>The entry involved</param>
        /// <param name='success'>True if the operation succeeeded, false otherwise</param>
        /// <param name='errorMessage'>An error message if the operation did not succeeed</param>
        void RegisterPut(IFileEntry entry, bool success, String errorMessage);

        /// <summary>
        /// Registers a delete operation
        /// </summary>
        /// <param name='entry'>The entry involved</param>
        /// <param name='success'>True if the operation succeeeded, false otherwise</param>
        /// <param name='errorMessage'>An error message if the operation did not succeeed</param>
        void RegisterDelete(IFileEntry entry, bool success, String errorMessage);

        /// <summary>
        /// Registers a create folder operation
        /// </summary>
        /// <param name='success'>True if the operation succeeeded, false otherwise</param>
        /// <param name='errorMessage'>An error message if the operation did not succeeed</param>
        void RegisterCreateFolder(bool success, String errorMessage);

        /// <summary>
        /// Registers a list operation
        /// </summary>
        /// <param name='files'>The entries returned, if the operation suceeded, null otherwise</param>
        /// <param name='success'>True if the operation succeeeded, false otherwise</param>
        /// <param name='errorMessage'>An error message if the operation did not succeeed</param>
        void RegisterList(List<Duplicati.Library.Interface.IFileEntry> files, bool success, String errorMessage);
    }
}

