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
    /// This interface extends the bare-minimum backend interface with some common methods
    /// </summary>
    public interface IBackend_v2 : IBackend
    {
        /// <summary>
        /// The purpose of this method is to test the connection to the remote backend.
        /// If any problem is encountered, this method should throw an exception.
        /// If the encountered problem is a missing target &quot;folder&quot;,
        /// this method should throw a <see cref="FolderMissingException"/>.
        /// </summary>
        void Test();

        /// <summary>
        /// The purpose of this method is to create the underlying &quot;folder&quot;.
        /// This method will be invoked if the <see cref="Test"/> method throws a
        /// <see cref="FolderMissingException"/>. 
        /// Backends that have no &quot;folder&quot; concept should not throw
        /// a <see cref="FolderMissingException"/> during <see cref="Test"/>, 
        /// and this method should throw a <see cref="MissingMethodException"/>.
        /// </summary>
        void CreateFolder();

    }
}
