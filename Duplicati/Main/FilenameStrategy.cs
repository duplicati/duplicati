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

namespace Duplicati.Main
{
    /// <summary>
    /// This class is responsible for naming remote files,
    /// and parsing remote filenames
    /// </summary>
    internal class FilenameStrategy
    {
        private bool m_useShortFilenames;
        private string m_timeSeperator;

        public FilenameStrategy(Dictionary<string, string> options)
        {
            //--short-filenames
            //--time-separator
            m_useShortFilenames = options.ContainsKey("short-filenames");
            if (options.ContainsKey("time-seperator"))
                m_timeSeperator = options["time-seperator"];
            else
                m_timeSeperator = ":";
        }

        public string GenerateFilename(string prefix, bool signatures, bool full, DateTime time, int volume)
        {
            return GenerateFilename(prefix, signatures, full, time) + ".vol" + volume.ToString();
        }

        public string GenerateFilename(string prefix, bool signatures, bool full, DateTime time)
        {
            if (!m_useShortFilenames)
            {
                string datetime = time.ToString().Replace(":", m_timeSeperator);
                return prefix + "-" + (signatures ? "signatures" : "content") + "-" + (full ? "full" : "inc") + "." + datetime;
            }
            else
            {
                //TODO: Finish this
                byte[] tmp = new byte[4 + 2 + 2 + 2 + 2 + 2];
                return prefix + "-" + (signatures ? "S" : "C") + (full ? "F" : "I") + "";
            }
        }

        public BackupEntry DecodeFilename(string prefix, Duplicati.Backend.FileEntry fe)
        {
            //TODO: Use RegExp to parse it
            //Filename looks like: "<prefix>-<content/signatures>-<full/inc>-<basename>.<date>.zip.pgp"
            //or
            //"<prefix>-<C/S><F/I>.<short date>.zip.pgp"
            if (!fe.Name.StartsWith(prefix))
                return null;
            string c = fe.Name.Substring(prefix.Length + 1);

            bool isContent = false;
            bool isFull = false;
            DateTime time;

            if (m_useShortFilenames)
            {
                //TODO: Finish this
                return null;
            }
            else
            {
                if (c.StartsWith("content"))
                    isContent = true;
                else if (!c.StartsWith("signatures"))
                    return null;

                c = c.Substring((isContent ? "content" : "signatures").Length + 1);

                if (c.StartsWith("full"))
                    isFull = true;
                else if (!c.StartsWith("inc"))
                    return null;

                c = c.Substring((isFull ? "full" : "inc").Length + 1);

                try
                {
                    string datestring = c.Substring(0, c.IndexOf(".")).Replace(m_timeSeperator, ":");
                    time = DateTime.Parse(datestring);
                }
                catch
                {
                    return null;
                }
            }
            return new BackupEntry(fe, time, isContent, isFull);
        }

    }
}
