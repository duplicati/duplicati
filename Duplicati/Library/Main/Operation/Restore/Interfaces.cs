namespace Duplicati.Library.Main.Operation.Restore
{
    public class BlockRequest(long blockID, long blockOffset, string blockHash, long blockSize, long volumeID)
    { // Total = 76 bytes
        public long BlockID { get; } = blockID;
        public long BlockOffset { get; } = blockOffset;
        public string BlockHash { get; } = blockHash;
        public long BlockSize { get; } = blockSize;
        public long VolumeID { get; } = volumeID;
    }
}