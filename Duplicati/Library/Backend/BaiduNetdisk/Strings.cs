using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class NetdiskBackend
    {
        public static string DisplayName { get { return LC.L(@"Baidu Netdisk"); } }

        public static string Description { get { return LC.L(@"Baidu Web disk provides you with network backup, synchronization and sharing of files. Large space, fast speed, safe and stable, support education network acceleration, support mobile terminal. Sign up now for a chance to enjoy 2TB of free storage. By 2021, Baidu online disk for eight years, the market share of more than 85%, with 700 million registered users."); } }

        public static string BaiduNetdiskAccountDescriptionShort { get { return LC.L(@"Authorization Code"); } }

        public static string BaiduNetdiskAccountDescriptionLong { get { return LC.L(@"Baidu Netdisk authorization code, please obtain authorization through the link above."); } }

        public static string BaiduNetdiskBlockSizeDescriptionShort { get { return LC.L(@"Block Size"); } }

        public static string BaiduNetdiskBlockSizeDescriptionLong { get { return LC.L(@"For common users, the size of a single fragment is fixed at 4MB, and the maximum size of a single file is 4G. The maximum size of a single fragment for ordinary members is 16MB and the maximum size of a single file is 10G. The maximum size of a single fragment for Super Member users is 32MB and the maximum size of a single file is 20GB."); } }
    }
}
