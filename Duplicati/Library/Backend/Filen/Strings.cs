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
    internal static class FilenBackend
    {
        public static string Description => LC.L(@"This backend can read and write data to Filen.io using its REST protocol. Allowed format is ""filen://folder/subfolder"".");
        public static string DisplayName => LC.L(@"Local folder or drive");
        public static string TwoFactorShort => LC.L(@"Optional 2-factor code");
        public static string TwoFactorLong => LC.L(@"The 2-factor code to use for authentication, leave empty if the account is not MFA protected. Not that a new code must be provided by the user for each authentication attempt.");
        public static string MoveToTrashShort => LC.L(@"Move to trash");
        public static string MoveToTrashLong => LC.L(@"If set, files will be moved to the trash instead of being deleted permanently.");
    }
}
