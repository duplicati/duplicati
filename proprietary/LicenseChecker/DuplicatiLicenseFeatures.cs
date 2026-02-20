// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.LicenseChecker;

/// <summary>
/// The console-specific license features
/// </summary>
public static class DuplicatiLicenseFeatures
{

    /// <summary>
    /// The feature indicating support for Office365 user backups
    /// </summary>
    public const string Office365Users = "duplicati:office365:users";

    /// <summary>
    /// The feature indicating support for Office365 group backups
    /// </summary>
    public const string Office365Groups = "duplicati:office365:groups";

    /// <summary>
    /// The feature indicating support for Office365 site backups
    /// </summary>
    public const string Office365Sites = "duplicati:office365:sites";

    /// <summary>
    /// The feature indicating support for Google Workspace user backups
    /// </summary>
    public const string GoogleWorkspaceUsers = "duplicati:googleworkspace:users";

    /// <summary>
    /// The feature indicating support for Google Workspace group backups
    /// </summary>
    public const string GoogleWorkspaceGroups = "duplicati:googleworkspace:groups";

    /// <summary>
    /// The feature indicating support for Google Workspace shared drive backups
    /// </summary>
    public const string GoogleWorkspaceSharedDrives = "duplicati:googleworkspace:shareddrives";

    /// <summary>
    /// The feature indicating support for Google Workspace site backups
    /// </summary>
    public const string GoogleWorkspaceSites = "duplicati:googleworkspace:sites";
}
