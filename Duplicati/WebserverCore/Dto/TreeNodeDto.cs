// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
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