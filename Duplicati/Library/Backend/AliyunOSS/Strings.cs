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
