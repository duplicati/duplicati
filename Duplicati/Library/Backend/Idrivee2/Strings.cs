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
    internal static class Idrivee2Backend
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to IDrive e2. Allowed format is ""e2://bucket/folder""."); } }
        public static string DisplayName { get { return LC.L(@"IDrive e2"); } }
        public static string KeySecretDescriptionLong { get { return LC.L(@"Access Key Secret can be obtained after logging into your IDrive e2 account. This can also be supplied through the option --{0}.", "auth-password"); } }
        public static string KeySecretDescriptionShort { get { return LC.L(@"Access Key Secret"); } }
        public static string KeyIDDescriptionLong { get { return LC.L(@"Access Key ID can be obtained after logging into your IDrive e2 account. This can also be supplied through the option --{0}.", "auth-username"); } }
        public static string KeyIDDescriptionShort { get { return LC.L(@"Access Key ID"); } }

        public static string BucketNameOrPathDescriptionLong { get { return LC.L(@"The ""Bucket Name or Complete Path"" is name of target bucket or complete of a folder inside the bucket."); } }
        public static string BucketNameOrPathDescriptionShort { get { return LC.L(@"The ""Bucket Name or Complete Path"""); } }

        public static string NoKeySecretError { get { return LC.L(@"No Access Key Secret given"); } }
        public static string NoKeyIdError { get { return LC.L(@"No Access Key ID given"); } }
    }
}
