using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings {
    internal static class Idrivee2Backend {
        public static string KeySecretDescriptionLong { get { return LC.L(@"The ""Access Key Secret "" can be obtained after logging into your IDrive e2 account, this can also be supplied through the ""auth-password"" property."); } }
        public static string KeySecretDescriptionShort { get { return LC.L(@"The ""Access Key Secret"""); } }
        public static string KeyIDDescriptionLong { get { return LC.L(@"The ""Access Key ID"" can be obtained after logging into your IDrive e2 account., this can also be supplied through the ""auth-username"" property."); } }
        public static string KeyIDDescriptionShort { get { return LC.L(@"The ""Access Key ID"""); } }

        public static string BucketNameOrPathDescriptionLong { get { return LC.L(@"The ""Bucket Name or Complete Path"" is name of target bucket or complete of a folder inside the bucket."); } }
        public static string BucketNameOrPathDescriptionShort { get { return LC.L(@"The ""Bucket Name or Complete Path"""); } }

        public static string DisplayName { get { return LC.L(@"IDrive e2"); } }
        public static string NoKeySecretError { get { return LC.L(@"No Access key secret given"); } }
        public static string NoKeyIdError { get { return LC.L(@"No Access key Id given"); } }
        public static string Description { get; set; }
    }
}
