namespace Duplicati.Library.Backend;

internal record TahoeNode
{
    public string? RwUri { get; set; }
    public string? VerifyUri { get; set; }
    public string? RoUri { get; set; }
    public Dictionary<string, TahoeEl>? Children { get; set; }
    public bool Mutable { get; set; }
    public long Size { get; set; }
    public TahoeMetadata? Metadata { get; set; }
}