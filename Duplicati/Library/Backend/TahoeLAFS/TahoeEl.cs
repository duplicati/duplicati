namespace Duplicati.Library.Backend;

internal record TahoeEl
{
    public string? Nodetype { get; set; }
    public TahoeNode? Node { get; set; }
}