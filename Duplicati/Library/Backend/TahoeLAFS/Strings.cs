// Copyright (C) 2025, The Duplicati Team
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
namespace Duplicati.Library.Backend.Strings {
    internal static class TahoeBackend {
        public static string Description { get { return LC.L(@"This backend can read and write data to a Tahoe-LAFS based backend. Allowed format is ""tahoe://hostname:port/uri/$DIRCAP""."); } }
        public static string Displayname { get { return LC.L(@"Tahoe-LAFS"); } }
        public static string DescriptionUseSSLLong { get { return LC.L(@"Use this option to communicate using Secure Socket Layer (SSL) over http (https)."); } }
        public static string DescriptionUseSSLShort { get { return LC.L(@"Instruct Duplicati to use an SSL (https) connection"); } }
        public static string MissingFolderError(string foldername, string message) { return LC.L(@"The folder {0} was not found, message: {1}", foldername, message); }
        public static string UnrecognizedUriError { get { return LC.L(@"Unsupported URL format, must start with ""uri/URI:DIR2:"""); } }
    }
}
