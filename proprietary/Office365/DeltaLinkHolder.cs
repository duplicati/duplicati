// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.Office365;

/// <summary>
/// Receives the <c>@odata.deltaLink</c> a Graph delta walk ends with. The walk streams its
/// items, so the link is not known until the enumeration completes; the holder lets the
/// caller read it afterwards without buffering the items.
/// </summary>
internal sealed class DeltaLinkHolder
{
    /// <summary>
    /// The delta link, set when the walk reaches its last page; <c>null</c> until then
    /// </summary>
    public string? DeltaLink { get; set; }
}
