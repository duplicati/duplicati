using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class AliyunDriveBackend
    {
        public static string DisplayName { get { return LC.L(@"Aliyun Drive"); } }

        public static string Description { get { return LC.L(@"Aliyun Drive to provide you with file network backup, synchronization and sharing services, free of charge without speed limit."); } }

        public static string AliyunDriveAccountDescriptionShort { get { return LC.L(@"Authorization Code"); } }

        public static string AliyunDriveAccountDescriptionLong { get { return LC.L(@"Aliyun Drive Authorization Code, please obtain authorization through the above link."); } }
    }
}
