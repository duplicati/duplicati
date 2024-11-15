using CoCoL;
using Duplicati.Library.Utility;
using static Duplicati.Library.Main.BackendManager;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal static class Channels
    {
        // TODO Should maybe come from Options, or at least some global configuration file?
        private static readonly int bufferSize = 128;

        public static readonly ChannelMarkerWrapper<Database.LocalRestoreDatabase.IFileToRestore> filesToRestore = new(new ChannelNameAttribute("filesToRestore", bufferSize));
        public static readonly ChannelMarkerWrapper<BlockRequest> downloadRequest = new(new ChannelNameAttribute("downloadRequest", bufferSize));
        public static readonly ChannelMarkerWrapper<(BlockRequest, IDownloadWaitHandle)> downloadedVolume = new(new ChannelNameAttribute("downloadResponse", bufferSize));
        public static readonly ChannelMarkerWrapper<(BlockRequest, TempFile)> decryptedVolume = new(new ChannelNameAttribute("decrytedVolume", bufferSize));
        public static readonly ChannelMarkerWrapper<(BlockRequest, byte[])> decompressedVolumes = new(new ChannelNameAttribute("decompressedVolumes", bufferSize));
    }
}