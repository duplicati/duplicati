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
    internal static class Filejump
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to filejump. Allowed formats are ""filejump://hostname/folder"" and ""filejump://username:password@hostname/folder""."); } }
        public static string DisplayName { get { return LC.L(@"Filejump"); } }
        public static string DescriptionApitokenShort { get { return LC.L(@"The filejump API token"); } }
        public static string DescriptionApitokenLong { get { return LC.L(@"Supply the filejump API token instead of the username and password. Can be obtained from: ""https://drive.filejump.com/account-settings"""); } }
        public static string DescriptionApiurlShort { get { return LC.L(@"The filejump API URL"); } }
        public static string DescriptionApiurlLong { get { return LC.L(@"Set the filejump API URL if using a non-standard url."); } }
        public static string DescriptionPagesizeShort { get { return LC.L(@"The filejump page size"); } }
        public static string DescriptionPagesizeLong { get { return LC.L(@"Adjusts the filejump API page size."); } }
        public static string DescriptionSoftDeleteShort { get { return LC.L(@"Use soft delete"); } }
        public static string DescriptionSoftDeleteLong { get { return LC.L(@"Use soft delete instead of hard delete."); } }
    }
}
