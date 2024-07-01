namespace Duplicati.WebserverCore.Abstractions;

public class FileEntry
{
    public string Path { get; set; } = "";
    public string MD5 { get; set; } = "";
    public string SHA256 { get; set; } = "";
    public DateTime? LastWriteTime { get; set; }
    public bool Ignore { get; set; }
}