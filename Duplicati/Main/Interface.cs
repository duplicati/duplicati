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
    public static class Interface
    {
        public static void Backup(string source, string target, Dictionary<string, string> options)
        {
            if (options.ContainsKey("inc") || options.ContainsKey("incremental"))
            {

            }
        }

        public static void Restore(string source, string taget, Dictionary<string, string> options)
        {
        }

        public static string[] List(string source, Dictionary<string, string> options)
        {
            List<string> res = new List<string>();
            Duplicati.Backend.IBackendInterface i = new Duplicati.Backend.BackendLoader(source);
            foreach (Duplicati.Backend.FileEntry fe in i.List(source, options))
                res.Add(fe.Name);

            return res.ToArray();
        }
    }
}
