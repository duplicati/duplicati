using CoCoL;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal static class Channels
    {
        public static readonly ChannelMarkerWrapper<Database.IRemoteVolume> downloadRequest = new(new ChannelNameAttribute("downloadRequest"));
        public static readonly ChannelMarkerWrapper<IAsyncDownloadedFile> downloadedVolume = new(new ChannelNameAttribute("downloadResponse"));
    }
}