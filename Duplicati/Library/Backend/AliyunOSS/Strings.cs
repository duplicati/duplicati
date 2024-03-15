using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class OSSBackend
    {
        public static string DisplayName { get { return LC.L(@"Aliyun OSS"); } }
        public static string Description { get { return LC.L(@"Aliyun Object Storage Service (OSS) is a massive, secure, low-cost, and highly reliable cloud storage service, offering up to 99.995% service availability."); } }

        public static string OSSAccessKeyIdDescriptionShort { get { return LC.L(@"Access Key Id"); } }
        public static string OSSAccessKeyIdDescriptionLong { get { return LC.L(@"AccessKeyId is used to identify the user."); } }

        public static string OSSAccessKeySecretDescriptionShort { get { return LC.L(@"Access Key Secret"); } }
        public static string OSSAccessKeySecretDescriptionLong { get { return LC.L(@"AccessKeySecret is the key used by the user to encrypt signature strings and by OSS to verify these signature strings"); } }

        public static string OSSBucketNameDescriptionShort { get { return LC.L(@"Bucket Name"); } }
        public static string OSSBucketNameDescriptionLong { get { return LC.L(@"A storage space is a container used to store objects (Object), and all objects must belong to a specific storage space."); } }

        public static string OSSRegionDescriptionShort { get { return LC.L(@"Region"); } }
        public static string OSSRegionDescriptionLong { get { return LC.L(@"Region indicates the physical location of the OSS data center."); } }

        public static string OSSEndpointDescriptionShort { get { return LC.L(@"Endpoint"); } }
        public static string OSSEndpointDescriptionLong { get { return LC.L(@"Endpoint refers to the domain name through which OSS provides external services."); } }
    }

}
