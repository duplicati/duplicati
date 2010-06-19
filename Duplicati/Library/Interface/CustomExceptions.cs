#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
    /// An exception indicating that the requested folder is missing
    /// </summary>
    public class FolderMissingException : Exception
    {
        public FolderMissingException()
            : base()
        { }

        public FolderMissingException(string message)
            : base(message)
        { }

        public FolderMissingException(Exception innerException)
            : base(Strings.Common.FolderMissingError, innerException)
        { }

        public FolderMissingException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }

    /// <summary>
    /// An exception indicating that the requested folder already existed
    /// </summary>
    public class FolderAreadyExistedExcpetion : Exception
    {
        public FolderAreadyExistedExcpetion()
            : base()
        { }

        public FolderAreadyExistedExcpetion(string message)
            : base(message)
        { }

        public FolderAreadyExistedExcpetion(Exception innerException)
            : base(Strings.Common.FolderAlreadyExistsError, innerException)
        { }

        public FolderAreadyExistedExcpetion(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
