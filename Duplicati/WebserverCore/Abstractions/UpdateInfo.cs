namespace Duplicati.WebserverCore.Abstractions;

public class UpdateInfo
{
    public string Displayname { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime? ReleaseTime { get; set; }
    public string ReleaseType { get; set; } = "";
    public string UpdateSeverity { get; set; } = "";
    public string ChangeInfo { get; set; } = "";
    public long CompressedSize { get; set; }
    public long UncompressedSize { get; set; }
    public string SHA256 { get; set; } = "";
    public string MD5 { get; set; } = "";
    public string[] RemoteURLS { get; set; } = [];
    public FileEntry[] Files { get; set; } = [];
}