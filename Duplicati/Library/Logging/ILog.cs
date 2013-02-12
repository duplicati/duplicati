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

namespace Duplicati.Library.Logging
{
    /// <summary>
    /// Interface for a loggin destination
    /// </summary>
    public interface ILog
    {
        /// <summary>
        /// The function called when a message is logged
        /// </summary>
        /// <param name="message">The message logged</param>
        /// <param name="type">The type of message logged</param>
        /// <param name="exception">An exception, may be null</param>
        void WriteMessage(string message, LogMessageType type, Exception exception);

        /// <summary>
        /// An event that is raised when a message is logged
        /// </summary>
        event EventLoggedDelgate EventLogged;
    }
}
