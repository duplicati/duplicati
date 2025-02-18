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
namespace Duplicati.Library.Backend.Strings
{
    internal static class Box {
        public static string Description { get { return LC.L(@"This backend can read and write data to Box.com. Allowed format is ""box://folder/subfolder""."); } }
        public static string DisplayName { get { return LC.L(@"Box.com"); } }
        public static string AuthidLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }
        public static string AuthidShort { get { return LC.L(@"The authorization code"); } }
        public static string ReallydeleteLong { get { return LC.L(@"After deleting a file, it may end up in the trash folder where it will be deleted after a grace period. Use this command to force immediate removal of delete files."); } }
        public static string ReallydeleteShort { get { return LC.L(@"Force delete files"); } }
    }
}
