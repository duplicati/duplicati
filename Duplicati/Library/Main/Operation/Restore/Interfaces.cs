namespace Duplicati.Library.Main.Operation.Restore
{
    public class BlockRequest(long blockID, string blockHash, long blockSize, long volumeID)
    { // Total = 68 bytes
        public long BlockID { get; } = blockID;
        public string BlockHash { get; } = blockHash;
        public long BlockSize { get; } = blockSize;
        public long VolumeID { get; } = volumeID;
    }
}