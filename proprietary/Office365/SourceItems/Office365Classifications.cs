// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.Office365.SourceItems;

/// <summary>
/// The user classifications that can be included or excluded from the backup.
/// Mirrors <see cref="SourceProvider.UserSeatCategory"/>.
/// </summary>
[Flags]
internal enum Office365UserClassification
{
    /// <summary>A regular user mailbox with one or more assigned licenses.</summary>
    Licensed = 1,
    /// <summary>A regular user mailbox with no assigned license.</summary>
    Unlicensed = 2,
    /// <summary>A shared/room/equipment mailbox with additional (licensed) storage.</summary>
    SharedMailboxWithStorage = 4,
    /// <summary>A shared/room/equipment mailbox without additional storage.</summary>
    SharedMailboxWithoutStorage = 8
}

/// <summary>
/// The group classifications that can be included or excluded from the backup.
/// </summary>
[Flags]
internal enum Office365GroupClassification
{
    /// <summary>A Microsoft 365 (Unified) group.</summary>
    Unified = 1,
    /// <summary>A security group or distribution list (non-Unified group).</summary>
    NotUnified = 2
}

/// <summary>
/// The site classifications that can be included or excluded from the backup.
/// Mirrors <see cref="SourceProvider.SiteCategory"/>.
/// </summary>
[Flags]
internal enum Office365SiteClassification
{
    /// <summary>A Microsoft 365 group-connected team site.</summary>
    Group = 1,
    /// <summary>A classic (non-group) team site.</summary>
    Classic = 2,
    /// <summary>A modern communication site.</summary>
    Communication = 4,
    /// <summary>A personal (OneDrive for Business) site.</summary>
    Personal = 8,
    /// <summary>Any other or undetermined site type.</summary>
    Other = 16
}
