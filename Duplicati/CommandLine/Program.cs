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

namespace Duplicati.CommandLine
{
    class Program
    {
        static void Main(string[] args)
        {
            Duplicati.SharpRSync.ChecksumFile cs1;
            using(System.IO.FileStream fs = System.IO.File.OpenRead("test signature.sig"))
                cs1 = new Duplicati.SharpRSync.ChecksumFile(fs);

            Duplicati.SharpRSync.ChecksumFile cs2 = new Duplicati.SharpRSync.ChecksumFile();
            using (System.IO.FileStream fs = System.IO.File.OpenRead("local\\Client.c"))
                cs2.AddStream(fs);

            using (System.IO.FileStream fs = System.IO.File.Create("test signature2.sig"))
                cs2.Save(fs);

            List<string> cargs = new List<string>(args);
            cargs.Add(Core.FilenameFilter.EncodeAsFilter(Core.FilenameFilter.ParseCommandLine(cargs, true)));
            Dictionary<string, string> options = CommandLineParser.ExtractOptions(cargs);

            string source = cargs[0];
            string target = cargs[1];

            if (source.Trim().ToLower() == "list")
                Console.WriteLine(string.Join("\r\n", Duplicati.Main.Interface.List(target, options)));
            else if (source.IndexOf("://") > 0)
                Duplicati.Main.Interface.Restore(source, target, options);
            else
                Duplicati.Main.Interface.Backup(source, target, options);
        }
    }
}
