// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.Office365.SourceItems;

/// <summary>
/// The user classifications that can be included or excluded from the backup.
/// Mirrors <see cref="SourceProvider.UserCategory"/>.
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
    /// <summary>
    /// A personal (OneDrive for Business) site owned by a licensed user account, or one whose
    /// owner could not be determined.
    /// </summary>
    PersonalLicensedUser = 8,
    /// <summary>Any other or undetermined site type.</summary>
    Other = 16,
    /// <summary>
    /// A personal (OneDrive for Business) site owned by a user account without an assigned
    /// Microsoft 365 license.
    /// </summary>
    PersonalUnlicensedUser = 32,
    /// <summary>
    /// Every personal (OneDrive for Business) site, regardless of the licensing state of the
    /// owning account. This is an alias, retained so that a configuration written before the
    /// owner was taken into account keeps selecting every personal site, and so that a caller
    /// that does not care about the owner can say so in one word.
    /// </summary>
    Personal = PersonalLicensedUser | PersonalUnlicensedUser
}
