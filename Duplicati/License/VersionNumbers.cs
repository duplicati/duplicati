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

namespace Duplicati.License
{
    /// <summary>
    /// The version numbers for the application, read from the assembly
    /// </summary>
    public static class VersionNumbers
    {
        /// <summary>
        /// The Version Tag read from the embedded resource VersionTag.txt
        /// </summary>
        public static readonly string TAG;
        /// <summary>
        /// The version number of the assembly
        /// </summary>
        public static readonly string VERSION_NAME;

        /// <summary>
        /// Static constructor to read the version numbers from the assembly
        /// </summary>
        static VersionNumbers()
        {
            string tag = "";
            try
            {
                using (var rd = new System.IO.StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(VersionNumbers), "VersionTag.txt")))
                    tag = rd.ReadToEnd();
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(tag))
                tag = "";

            TAG = tag.Trim();

            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            if (!string.IsNullOrWhiteSpace(TAG))
                v = " - " + TAG;
#if DEBUG
            v = " - debug";
#endif
            VERSION_NAME = v;

        }
    }
}
