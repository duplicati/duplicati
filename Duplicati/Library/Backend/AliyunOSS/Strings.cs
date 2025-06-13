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
    internal static class OSSBackend
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to Aliyun OSS."); } }
        public static string DisplayName { get { return LC.L(@"Aliyun OSS (Object Storage Service)"); } }
        public static string OSSAccessKeyIdDescriptionLong { get { return LC.L(@"Access Key ID is used to identify the user."); } }
        public static string OSSAccessKeyIdDescriptionShort { get { return LC.L(@"Access Key ID"); } }
        public static string OSSAccessKeySecretDescriptionLong { get { return LC.L(@"Access Key Secret is the key used by the user to encrypt signature strings and by OSS to verify these signature strings."); } }
        public static string OSSAccessKeySecretDescriptionShort { get { return LC.L(@"Access Key Secret"); } }
        public static string OSSBucketNameDescriptionLong { get { return LC.L(@"A storage space is a container used to store objects (Object), and all objects must belong to a specific storage space."); } }
        public static string OSSBucketNameDescriptionShort { get { return LC.L(@"Bucket name"); } }
        public static string OSSRegionDescriptionLong { get { return LC.L(@"Region indicates the physical location of the OSS data center."); } }
        public static string OSSRegionDescriptionShort { get { return LC.L(@"Region"); } }
        public static string OSSEndpointDescriptionLong { get { return LC.L(@"Endpoint refers to the domain name through which OSS provides external services."); } }
        public static string OSSEndpointDescriptionShort { get { return LC.L(@"Endpoint"); } }
    }

}
