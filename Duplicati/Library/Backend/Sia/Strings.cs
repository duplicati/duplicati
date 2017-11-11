using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings
{
    internal static class Sia
    {
        public static string DisplayName { get { return LC.L(@"Sia Decentralized Cloud"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to Sia."); } }
        public static string SiaHostDescriptionShort { get { return LC.L(@"Sia address"); } }
        public static string SiaHostDescriptionLong { get { return LC.L(@"Sia address, ie 127.0.0.1:9980"); } }
        public static string SiaPathDescriptionShort { get { return LC.L(@"Backup path"); } }
        public static string SiaPathDescriptionLong { get { return LC.L(@"Target path, ie /backup"); } }
        public static string SiaPasswordShort { get { return LC.L(@"Sia password"); } }
        public static string SiaPasswordLong { get { return LC.L(@"Sia password"); } }
        public static string SiaRedundancyDescriptionShort { get { return LC.L(@"3"); } }
        public static string SiaRedundancyDescriptionLong { get { return LC.L(@"Minimum value is 3."); } }
    }
}
