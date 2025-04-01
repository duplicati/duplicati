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
    internal static class MegaBackend {
        public static string Description { get { return LC.L(@"This backend can read and write data to Mega.co.nz. Allowed format is ""mega://folder/subfolder""."); } }
        public static string DisplayName { get { return LC.L(@"mega.nz"); } }
        public static string AuthPasswordDescriptionLong { get { return LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD""."); } }
        public static string AuthPasswordDescriptionShort { get { return LC.L(@"Supply the password used to connect to the server"); } }
        public static string AuthUsernameDescriptionLong { get { return LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME""."); } }
        public static string AuthUsernameDescriptionShort { get { return LC.L(@"Supply the username used to connect to the server"); } }
        public static string AuthTwoFactorKeyDescriptionLong { get { return LC.L(@"For accounts with two-factor authentication enabled, set the shared secret used to generate the two-factor TOTP codes."); } }
        public static string AuthTwoFactorKeyDescriptionShort { get { return LC.L(@"The shared secret used to generate two-factor TOTP codes"); } }
        public static string NoPasswordError { get { return LC.L(@"No password given"); } }
        public static string NoUsernameError { get { return LC.L(@"No username given"); } }
    }
}
