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
    internal static class pCloudBackend {
        
        public static string Description => LC.L(@"This backend can read and write data to pCloud with native API. Allowed format is ""pcloud://api.pcloud.com"".");
        public static string DisplayName => LC.L(@"pCloud (Native API)");
        public static string AuthPasswordDescriptionLong => LC.L(@"The oAuth token used to connect to the server. This may also be supplied as the environment variable ""AUTHID"".");
        public static string AuthPasswordDescriptionShort => LC.L(@"Supply the oAuth token used to connect to the server");
        public static string NoServerSpecified => LC.L(@"No server specified, must be either api.pcloud.com or eapi.pcloud.com for European hosting");
        public static string InvalidServerSpecified => LC.L(@"Invalid server specified, must be either api.pcloud.com or eapi.pcloud.com for European hosting");
        public static string FailedWithUnexpectedErrorCode(string operation, int resultcode) => LC.L(@"Operation {0} failed with unexpected result code: {1}", operation, resultcode);
    }
}
