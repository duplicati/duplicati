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
    internal static class CIFSBackend
    {

        public static string DescriptionAuthPasswordLong => LC.L(@"The password used to connect to the server. This may also be supplied as the environment variable ""AUTH_PASSWORD"".");
        public static string DescriptionAuthPasswordShort => LC.L(@"Supply the password used to connect to the server");
        public static string DescriptionAuthUsernameLong => LC.L(@"The username used to connect to the server. This may also be supplied as the environment variable ""AUTH_USERNAME"".");
        public static string DescriptionAuthUsernameShort => LC.L(@"Supply the username used to connect to the server");
        public static string DescriptionAuthDomainLong => LC.L(@"The domain used to connect to the server. This may also be supplied as the environment variable ""AUTH_DOMAIN"".");
        public static string DescriptionAuthDomainShort => LC.L(@"Supply the domain used to connect to the server");
        public static string Description => LC.L(@"This backend can read and write data to CIFS/SMB destinations. Allowed format is ""cifs://server/share"".");
        public static string DisplayName => LC.L(@"CIFS/SMB");
    }

    internal static class Options
    {
        public static string TransportShort =>
            LC.L(
                @"Defines the transport to be used in CIFS connection");
        public static string TransportLong =>
            LC.L(
                @"Defines the transport to be used in CIFS connection. Can be DirectTCP or NetBios");
        public static string DescriptionReadBufferSizeShort => LC.L(@"Read buffer size for SMB operations.");
        public static string DescriptionReadBufferSizeLong => LC.L(@"Read buffer size for SMB operations (Will be capped automatically by SMB negotiated values, values bellow 10000 bytes will be ignored)");
        public static string DescriptionWriteBufferSizeShort => LC.L(@"Write buffer size for SMB operations.");
        public static string DescriptionWriteBufferSizeLong => LC.L(@"Write buffer size for SMB operations (Will be capped automatically by SMB negotiated values, values bellow 10000 bytes will be ignored)");
    }
}
