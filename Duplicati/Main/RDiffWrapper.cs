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

namespace Duplicati.Main.RSync
{
    /// <summary>
    /// This class wraps the usage of RDiff.exe so the use of RDiff looks like native calls
    /// </summary>
    public class RDiffWrapper
    {
        /// <summary>
        /// The name of the RDiff program to call
        /// </summary>
        public static string RDIFF_PROGRAM = "rdiff";

        /// <summary>
        /// Generates a signature file
        /// </summary>
        /// <param name="filename">The file to create the signature for</param>
        /// <param name="outputfile">The file write the signature to</param>
        public static void GenerateSignature(string filename, string outputfile)
        {
            if (System.IO.File.Exists(outputfile))
                System.IO.File.Delete(outputfile);
            //System.IO.File.Create(outputfile);
            ReadProgramOutput("signature \"" + filename + "\" \"" + outputfile + "\"", null);
        }

        /// <summary>
        /// Generates a delta file
        /// </summary>
        /// <param name="signaturefile">The signature for the file</param>
        /// <param name="filename">The file to create the delta for</param>
        /// <param name="deltafile">The delta output file</param>
        /// <returns></returns>
        public static void GenerateDelta(string signaturefile, string filename, string deltafile)
        {
            if (System.IO.File.Exists(deltafile))
                System.IO.File.Delete(deltafile);
            ReadProgramOutput("delta \"" + signaturefile + "\" \"" + filename + "\" \"" + deltafile + "\"", null);
        }

        /// <summary>
        /// Patches a file
        /// </summary>
        /// <param name="basefile">The most recent full copy of the file</param>
        /// <param name="deltafile">The delta file</param>
        /// <param name="outputfile">The restored file</param>
        public static void PatchFile(string basefile, string deltafile, string outputfile)
        {
            if (System.IO.File.Exists(outputfile))
                System.IO.File.Delete(outputfile);
            ReadProgramOutput("patch \"" + basefile + "\" \"" + deltafile + "\" \"" + outputfile + "\"", null);
        }

        /// <summary>
        /// Internal helper function that wraps the usage of rdiff
        /// </summary>
        /// <param name="args">The command line arguments</param>
        /// <param name="output">An optional stream to write output to</param>
        /// <returns>The data from stdout</returns>
        private static byte[] ReadProgramOutput(string args, System.IO.Stream output)
        {
            System.Diagnostics.ProcessStartInfo pi = new System.Diagnostics.ProcessStartInfo(RDIFF_PROGRAM);
            pi.Arguments = args;
            pi.CreateNoWindow = true;
            pi.RedirectStandardOutput = true;
            pi.RedirectStandardError = true;
            pi.UseShellExecute = false;
            pi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

            System.Diagnostics.Process p = System.Diagnostics.Process.Start(pi);
            System.IO.Stream outstream = output;
            if (outstream == null)
                outstream = new System.IO.MemoryStream();
            
            byte[] buf = new byte[1024];
            int a;
            while ((a = p.StandardOutput.BaseStream.Read(buf, 0, buf.Length)) != 0)
                outstream.Write(buf, 0, a);
            p.WaitForExit();

            if (p.ExitCode != 0)
                throw new Exception(p.StandardError.ReadToEnd());
            else if (output == null)
                return (outstream as System.IO.MemoryStream).ToArray();
            else
                return null;

        }
    }
}
