namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// Represents a tree node
/// </summary>
public class TreeNodeDto
{
    /// <summary>
    /// The text displayed for the node
    /// </summary>
    public required string text { get; init; }
    /// <summary>
    /// The node id
    /// </summary>
    public required string id { get; init; }
    /// <summary>
    /// The class applied to the node
    /// </summary>
    public required string cls { get; init; }
    /// <summary>
    /// The class applied to the icon
    /// </summary>
    public required string iconCls { get; init; }
    /// <summary>
    /// True if the element should be checked
    /// </summary>
    public required bool check { get; init; }
    /// <summary>
    /// True if the element is a leaf node
    /// </summary>
    public required bool leaf { get; init; }
    /// <summary>
    /// Gets or sets the current path, if the item is a symbolic path
    /// </summary>
    public required string? resolvedpath { get; init; }
    /// <summary>
    /// True if the element is hidden
    /// </summary>
    public required bool hidden { get; init; }
    /// <summary>
    /// True if the element has the system file attribute
    /// </summary>
    public required bool systemFile { get; init; }
    /// <summary>
    /// True if the element is marked as temporary
    /// </summary>
    public required bool temporary { get; init; }
    /// <summary>
    /// True if the element is a symlink
    /// </summary>
    public required bool symlink { get; init; }
    /// <summary>
    /// Size of the file. -1 if directory or inaccessible
    /// </summary>
    public required long fileSize { get; init; }

    /// <summary>
    /// Constructs a new TreeNode
    /// </summary>
    public TreeNodeDto()
    {
        this.cls = "folder";
        this.iconCls = "x-tree-icon-parent";
        this.check = false;
    }
}