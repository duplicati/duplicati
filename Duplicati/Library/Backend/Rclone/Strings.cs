using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class Rclone
    {
        public static string DisplayName { get { return LC.L(@"Rclone"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to Rclone."); } }
        public static string RcloneLocalRepoShort { get { return LC.L(@"Local repository"); } }
        public static string RcloneLocalRepoLong { get { return LC.L(@"Local repository"); } }
        public static string RcloneRemoteRepoShort { get { return LC.L(@"Remote repository"); } }
        public static string RcloneRemoteRepoLong { get { return LC.L(@"Remote repository"); } }
        public static string RcloneRemotePathShort { get { return LC.L(@"Remote path"); } }
        public static string RcloneRemotePathLong { get { return LC.L(@"Remote path"); } }
        public static string RcloneOptionRcloneShort { get { return LC.L(@"Rclone options"); } }
        public static string RcloneOptionRcloneLong { get { return LC.L(@"Rclone options"); } }

    }
}
