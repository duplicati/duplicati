// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Parity.Strings
{
    internal static class Par2Parity
    {
        public static string DisplayName { get { return LC.L(@"PAR2 parity, external"); } }
        public static string Description { get { return LC.L(@"The PAR2 parity module creates error-correction data using the PAR2 standard, allowing damaged remote volumes to be repaired. It requires that the ""par2"" (par2cmdline) executable is available on the system. On Linux and macOS it can be installed via the package manager; the path can be supplied using the option --{0}.", "par2-program-path"); } }
        public static string Par2programpathShort { get { return LC.L(@"The path to the par2 program"); } }
        public static string Par2programpathLong { get { return LC.L(@"The path to the par2 (par2cmdline) program. If not supplied, Duplicati will search for ""par2"" on the system path."); } }
        public static string Par2extraoptionsShort { get { return LC.L(@"Extra options for the par2 program"); } }
        public static string Par2extraoptionsLong { get { return LC.L(@"Use this option to supply extra commandline options to the par2 program when creating parity data."); } }
        public static string Par2ExecuteError(string program, string args, string message) { return LC.L(@"Failed to execute par2 with ""{0} {1}"": {2}", program, args, message); }
        public static string Par2NotFound { get { return LC.L(@"The par2 program was not found; parity data will not be created. Install par2cmdline or set the --par2-program-path option."); } }
    }
}
