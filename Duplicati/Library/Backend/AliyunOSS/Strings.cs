using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class OSSBackend
    {
        public static string DisplayName { get { return LC.L(@"Aliyun OSS"); } }
        public static string Description { get { return LC.L(@"Aliyun OSS"); } }

        public static string OSSAccessKeyIdDescriptionShort { get { return LC.L(@"Secret Id"); } }
        public static string OSSAccessKeyIdDescriptionLong { get { return LC.L(@"Cloud API Secret Id"); } }

        public static string OSSAccessKeySecretDescriptionShort { get { return LC.L(@"Secret Key"); } }
        public static string OSSAccessKeySecretDescriptionLong { get { return LC.L(@"Cloud API Secret Key"); } }

        public static string OSSBucketNameDescriptionShort { get { return LC.L(@"Bucket"); } }
        public static string OSSBucketNameDescriptionLong { get { return LC.L(@"Bucket, format: BucketName-APPID"); } }

        public static string OSSRegionDescriptionShort { get { return LC.L(@"Specifies COS location constraints"); } }
        public static string OSSRegionDescriptionLong { get { return LC.L(@"Region (Region) is the distribution area of ​​the Tencent cloud hosting machine room, the object storage COS data is stored in the storage buckets of these regions. https://intl.cloud.tencent.com/document/product/436/6224"); } }

        public static string OSSEndpointDescriptionShort { get { return LC.L(@"xxx"); } }
        public static string OSSEndpointDescriptionLong { get { return LC.L(@"xxx"); } }
    }

}
