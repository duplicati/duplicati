using CoCoL;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal static class Channels
    {
        public static readonly ChannelMarkerWrapper<(long, Database.IRemoteVolume)> downloadRequest = new(new ChannelNameAttribute("downloadRequest"));
        public static readonly ChannelMarkerWrapper<(long, TempFile)> downloadedVolume = new(new ChannelNameAttribute("downloadResponse"));
        public static readonly ChannelMarkerWrapper<Database.LocalRestoreDatabase.IFileToRestore> filesToRestore = new(new ChannelNameAttribute("filesToRestore"));
        public static readonly ChannelMarkerWrapper<(long, byte[])> decompressedVolumes = new(new ChannelNameAttribute("decompressedVolumes"));
        public static readonly ChannelMarkerWrapper<(long, TempFile)> decryptedVolume = new(new ChannelNameAttribute("decrytedVolume"));
    }
}