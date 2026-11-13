// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using System.Runtime.CompilerServices;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class MetaRootSourceEntry(SourceProvider provider, string parentPath, string name, SourceItemType type)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, name)), null, null)
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<MetaRootSourceEntry>();

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            yield break;

        if (type == SourceItemType.MetaRootUsers)
        {
            var skippedArchivedUsers = 0;
            var skippedSuspendedUsers = 0;

            // Placing the users by organizational unit needs every user before the first
            // folder can be listed, so they are collected rather than listed as they arrive
            var orgUnitUsers = provider.Options.UseOuPaths ? new List<Google.Apis.Admin.Directory.directory_v1.Data.User>() : null;

            await foreach (var user in provider.ListAllUsersAsync(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                // Archived and suspended accounts are backed up by default, as their data
                // remains readable and they do not consume a seat, but can be opted out.
                var category = SourceProvider.ClassifyUser(user);
                if (category == SourceProvider.UserCategory.Archived && provider.Options.ExcludeArchivedUsers)
                {
                    skippedArchivedUsers++;
                    Log.WriteVerboseMessage(LOGTAG, "SkippingExcludedUser", Strings.SkippingExcludedUser(user.PrimaryEmail, category.ToString().ToLowerInvariant(), OptionsHelper.GOOGLE_EXCLUDE_ARCHIVED_USERS_OPTION));
                    continue;
                }
                if (category == SourceProvider.UserCategory.Suspended && provider.Options.ExcludeSuspendedUsers)
                {
                    skippedSuspendedUsers++;
                    Log.WriteVerboseMessage(LOGTAG, "SkippingExcludedUser", Strings.SkippingExcludedUser(user.PrimaryEmail, category.ToString().ToLowerInvariant(), OptionsHelper.GOOGLE_EXCLUDE_SUSPENDED_USERS_OPTION));
                    continue;
                }

                if (orgUnitUsers != null)
                {
                    orgUnitUsers.Add(user);
                    continue;
                }

                // Only active accounts consume a seat, which the directory object already
                // answers without any further request.
                var countsAsSeat = category == SourceProvider.UserCategory.Active;

                if (provider.LicenseApprovedForEntry(Path, GoogleRootType.Users, user.Id, increment: false, countsAsSeat: countsAsSeat))
                    yield return new UserSourceEntry(provider, this.Path, this.Path, user, user.PrimaryEmail);
            }

            if (skippedArchivedUsers > 0)
                Log.WriteInformationMessage(LOGTAG, "SkippedExcludedUsers", Strings.SkippedExcludedUsers(skippedArchivedUsers, "archived", OptionsHelper.GOOGLE_EXCLUDE_ARCHIVED_USERS_OPTION));
            if (skippedSuspendedUsers > 0)
                Log.WriteInformationMessage(LOGTAG, "SkippedExcludedUsers", Strings.SkippedExcludedUsers(skippedSuspendedUsers, "suspended", OptionsHelper.GOOGLE_EXCLUDE_SUSPENDED_USERS_OPTION));

            if (orgUnitUsers != null)
            {
                var root = OrgUnitNode.Build(await provider.ListAllOrgUnitsAsync(cancellationToken).ConfigureAwait(false), orgUnitUsers, []);
                await foreach (var entry in OrganizationalUnitFolderSourceEntry.EnumerateNode(provider, this.Path, this.Path, root, cancellationToken).ConfigureAwait(false))
                    yield return entry;
            }
        }
        else if (type == SourceItemType.MetaRootGroups)
        {
            await foreach (var group in provider.ListAllGroupsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                if (provider.LicenseApprovedForEntry(Path, GoogleRootType.Groups, group.Id, increment: false, countsAsSeat: true))
                    yield return new GroupSourceEntry(provider, this.Path, group);
            }
        }
        else if (type == SourceItemType.MetaRootSharedDrives)
        {
            var driveService = provider.ApiHelper.GetDriveService();
            if (provider.Options.UseOuPaths)
            {
                // The drives the backup reads are the ones listed without administrator
                // access; the administrator listing also returns drives the account is not
                // a member of, and is only used to learn the organizational unit of each drive
                var drives = new List<Google.Apis.Drive.v3.Data.Drive>();
                await foreach (var drive in SourceProvider.ListAllSharedDrivesAsync(driveService, cancellationToken).ConfigureAwait(false))
                    drives.Add(drive);

                var orgUnitIds = await provider.ListSharedDriveOrgUnitIdsAsync(driveService, cancellationToken).ConfigureAwait(false);
                foreach (var drive in drives)
                    drive.OrgUnitId = orgUnitIds.GetValueOrDefault(drive.Id);

                var root = OrgUnitNode.Build(await provider.ListAllOrgUnitsAsync(cancellationToken).ConfigureAwait(false), [], drives);
                await foreach (var entry in OrganizationalUnitFolderSourceEntry.EnumerateNode(provider, this.Path, this.Path, root, cancellationToken).ConfigureAwait(false))
                    yield return entry;
            }
            else
            {
                await foreach (var n in SharedDrivesSourceEntry.EnumerateSharedDrives(provider, this.Path, null, userIsInactive: false, driveService, cancellationToken))
                    yield return n;
            }
        }
        else if (type == SourceItemType.MetaRootSites)
        {
            var service = provider.ApiHelper.GetDriveService();
            var request = service.Files.List();
            request.Q = $"mimeType='{GoogleMimeTypes.Site}' and trashed=false";
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;
            // Request all drives so we do not get user drives only
            request.Corpora = "allDrives";

            string? nextPageToken = null;
            do
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                request.PageToken = nextPageToken;

                Google.Apis.Drive.v3.Data.FileList? files = null;
                try
                {
                    files = await request.ExecuteAsync(cancellationToken);
                }
                catch
                {
                    // Fallback or ignore
                    yield break;
                }

                if (files != null && files.Files != null)
                {
                    foreach (var file in files.Files)
                    {
                        if (cancellationToken.IsCancellationRequested) yield break;

                        if (provider.LicenseApprovedForEntry(Path, GoogleRootType.Sites, file.Id, increment: false, countsAsSeat: true))
                            yield return new SiteSourceEntry(provider, this.Path, file);
                    }
                }
                nextPageToken = files?.NextPageToken;
            } while (!string.IsNullOrEmpty(nextPageToken));
        }
        else if (type == SourceItemType.MetaRootOrganizationalUnits)
        {
            yield return new OrganizationalUnitsSourceEntry(provider, this.Path);
        }
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "gsuite:v", "1" },
                { "gsuite:Type", type.ToString() },
                { "gsuite:Name", name },
                { "gsuite:Id", type.ToString() }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
