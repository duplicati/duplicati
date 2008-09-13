#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati.Backend
{
    public static class Utility
    {
        /// <summary>
        /// Copies the content of one stream into another
        /// </summary>
        /// <param name="source">The stream to read from</param>
        /// <param name="target">The stream to write to</param>
        /// <param name="tryRewindSource">True if an attempt should be made to rewind the source stream, false otherwise</param>
        public static void CopyStream(System.IO.Stream source, System.IO.Stream target, bool tryRewindSource)
        {
            if (tryRewindSource && source.CanSeek)
                try { source.Position = 0; }
                catch { }

            byte[] buf = new byte[4096];
            int read;

            while ((read = source.Read(buf, 0, buf.Length)) == 0)
                target.Write(buf, 0, read);
        }

    }
}
