using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.Backend.Strings {
    internal static class Jottacloud
    {
        public static string DisplayName { get { return LC.L(@"Jottacloud"); } }
        public static string Description { get { return LC.L(@"This backend can read and write data to Jottacloud using it's REST protocol. Allowed format is ""jottacloud://folder/subfolder""."); } }
        public static string AuthidShort { get { return LC.L(@"The authorization code"); } }
        public static string AuthidLong(string url) { return LC.L(@"The authorization token retrieved from {0}", url); }
        public static string NoUsernameError { get { return LC.L(@"No username found"); } }
        public static string NoPathError { get { return LC.L(@"No path given, cannot upload files to the root folder"); } }
        public static string IllegalMountPoint { get { return LC.L(@"Illegal mount point given."); } }
        public static string FileUploadError { get { return LC.L(@"Failed to upload file"); } }
        public static string DescriptionDeviceShort { get { return LC.L(@"Supplies the backup device to use"); } }
        public static string DescriptionDeviceLong(string mountPointOption) { return LC.L(@"The backup device to use. Will be created if not already exists. You can manage your devices from the backup panel in the Jottacloud web interface. When you specify a custom device you should also specify the mount point to use on this device with the ""{0}"" option.", mountPointOption); }
        public static string DescriptionMountPointShort { get { return LC.L(@"Supplies the mount point to use on the server"); } }
        public static string DescriptionMountPointLong(string deviceOptionName) { return LC.L(@"The mount point to use on the server. The default is ""Archive"" for using the built-in archive mount point. Set this option to ""Sync"" to use the built-in synchronization mount point instead, or if you have specified a custom device with option ""{0}"" you are free to name the mount point as you like.", deviceOptionName); }
        public static string ThreadsShort { get { return LC.L(@"Number of threads for restore operations."); } }
        public static string ThreadsLong { get { return LC.L(@"Number of threads for restore operations. In some cases the download rate is limited to 18.5 Mbps per stream. Use multiple threads to increase throughput."); } }
        public static string ChunksizeShort { get { return LC.L(@"The chunk size for simultaneous downloading."); } }
        public static string ChunksizeLong { get { return LC.L(@"The chunk size for simultaneous downloading. These chunks will be held in memory, so keep it as low as possible."); } }
    }
}
