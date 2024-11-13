using CoCoL;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal static class Channels
    {
        // TODO Should maybe come from Options, or at least some global configuration file?
        private static int bufferSize = 128;

        public static readonly ChannelMarkerWrapper<(long, long, Database.IRemoteVolume)> downloadRequest = new(new ChannelNameAttribute("downloadRequest", bufferSize));
        public static readonly ChannelMarkerWrapper<(long, long, IDownloadWaitHandle)> downloadedVolume = new(new ChannelNameAttribute("downloadResponse", bufferSize));
        public static readonly ChannelMarkerWrapper<Database.LocalRestoreDatabase.IFileToRestore> filesToRestore = new(new ChannelNameAttribute("filesToRestore", bufferSize));
        public static readonly ChannelMarkerWrapper<(long, byte[])> decompressedVolumes = new(new ChannelNameAttribute("decompressedVolumes", bufferSize));
        public static readonly ChannelMarkerWrapper<(long, long, TempFile)> decryptedVolume = new(new ChannelNameAttribute("decrytedVolume", bufferSize));
    }
}