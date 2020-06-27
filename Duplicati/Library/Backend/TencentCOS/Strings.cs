using COSXML.Model.Bucket;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class COSBackend
    {
        public static string DisplayName { get { return LC.L(@"Tencent COS"); } }
        public static string Description { get { return LC.L(@"Object storage (Cloud Object Storage, COS) is a distributed storage service for storing massive files provided by Tencent Cloud, which has the advantages of high scalability, low cost, reliability and security."); } }

        public static string COSAccountDescriptionShort { get { return LC.L(@"Account ID"); } }
        public static string COSAccountDescriptionLong { get { return LC.L(@"Account ID of Tencent Cloud Account"); } }

        public static string COSAPISecretIdDescriptionShort { get { return LC.L(@"Secret Id"); } }
        public static string COSAPISecretIdDescriptionLong { get { return LC.L(@"Cloud API Secret Id"); } }

        public static string COSAPISecretKeyDescriptionShort { get { return LC.L(@"Secret Key"); } }
        public static string COSAPISecretKeyDescriptionLong { get { return LC.L(@"Cloud API Secret Key"); } }

        public static string COSBucketDescriptionShort { get { return LC.L(@"Bucket"); } }
        public static string COSBucketDescriptionLong { get { return LC.L(@"Bucket, format: BucketName-APPID"); } }

        public static string COSLocationDescriptionShort { get { return LC.L(@"Specifies COS location constraints"); } }
        public static string COSLocationDescriptionLong { get { return LC.L(@"Region (Region) is the distribution area of ​​the Tencent cloud hosting machine room, the object storage COS data is stored in the storage buckets of these regions. https://cloud.tencent.com/document/product/436/6224"); } }
    }

}
