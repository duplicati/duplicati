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
    internal static class COSBackend
    {
        public static string Description { get { return LC.L(@"This backend can read and write data to the Tencent COS."); } }
        public static string DisplayName { get { return LC.L(@"Tencent COS (Cloud Object Storage)"); } }
        public static string COSAccountDescriptionLong { get { return LC.L(@"Account ID of Tencent Cloud Account."); } }
        public static string COSAccountDescriptionShort { get { return LC.L(@"Account ID"); } }
        public static string COSAPISecretIdDescriptionLong { get { return LC.L(@"Cloud API Secret ID."); } }
        public static string COSAPISecretIdDescriptionShort { get { return LC.L(@"Secret ID"); } }
        public static string COSAPISecretKeyDescriptionLong { get { return LC.L(@"Cloud API Secret Key."); } }
        public static string COSAPISecretKeyDescriptionShort { get { return LC.L(@"Secret Key"); } }
        public static string COSBucketDescriptionLong { get { return LC.L(@"Bucket name, format: BucketName-APPID"); } }
        public static string COSBucketDescriptionShort { get { return LC.L(@"Bucket name"); } }
        public static string COSLocationDescriptionLong { get { return LC.L(@"Region is the distribution area of ​​the Tencent cloud hosting machine room. The object storage COS data is stored in the storage buckets of these regions. https://intl.cloud.tencent.com/document/product/436/6224."); } }
        public static string COSLocationDescriptionShort { get { return LC.L(@"Specify COS location constraints"); } }
        public static string COSStorageClassDescriptionLong { get { return LC.L(@"Storage class of the object; check enumerated values at https://intl.cloud.tencent.com/document/product/436/30925."); } }
        public static string COSStorageClassDescriptionShort { get { return LC.L(@"Storage class of the object"); } }
    }

}
