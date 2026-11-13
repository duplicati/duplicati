// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Drive = Google.Apis.Drive.v3.Data.Drive;
using OrgUnit = Google.Apis.Admin.Directory.directory_v1.Data.OrgUnit;
using User = Google.Apis.Admin.Directory.directory_v1.Data.User;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// An organizational unit in the tree the users or the shared drives are listed in when the
/// provider places them by organizational unit
/// </summary>
/// <param name="Id">The organizational unit id without its <c>id:</c> prefix, used as the folder name; empty for the root</param>
/// <param name="Name">The name of the organizational unit</param>
/// <param name="OrgUnitPath">The full path of the organizational unit, <c>/</c> for the root</param>
internal sealed record OrgUnitNode(string Id, string Name, string OrgUnitPath)
{
    /// <summary>
    /// The organizational units directly below this one
    /// </summary>
    public List<OrgUnitNode> Children { get; } = [];

    /// <summary>
    /// The users placed directly in this organizational unit
    /// </summary>
    public List<User> Users { get; } = [];

    /// <summary>
    /// The shared drives placed directly in this organizational unit
    /// </summary>
    public List<Drive> SharedDrives { get; } = [];

    /// <summary>
    /// The prefix of organizational unit ids in the Directory API
    /// </summary>
    private const string ID_PREFIX = "id:";

    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<OrgUnitNode>();

    /// <summary>
    /// Builds the tree of organizational units and places each user and shared drive in its
    /// organizational unit. A user whose organizational unit is not in the list, such as one
    /// created after the list was read, is placed in the nearest listed ancestor, or in the
    /// root. A shared drive is identified with its organizational unit by id only, so one whose
    /// organizational unit is not in the list is placed in the root.
    /// </summary>
    /// <param name="orgUnits">The organizational units of the tenant</param>
    /// <param name="users">The users to place</param>
    /// <param name="sharedDrives">The shared drives to place, with <see cref="Drive.OrgUnitId"/> set to their organizational unit</param>
    /// <returns>The root organizational unit</returns>
    public static OrgUnitNode Build(IEnumerable<OrgUnit> orgUnits, IEnumerable<User> users, IEnumerable<Drive> sharedDrives)
    {
        var root = new OrgUnitNode(string.Empty, "/", "/");
        var byPath = new Dictionary<string, OrgUnitNode>(StringComparer.OrdinalIgnoreCase) { ["/"] = root };

        // The organizational unit id is the folder name: it stays the same when the unit is
        // renamed or moved, and holds no characters a file system would reject
        var listed = orgUnits
            .Where(x => !string.IsNullOrWhiteSpace(x.OrgUnitPath) && !string.IsNullOrWhiteSpace(x.OrgUnitId))
            .ToList();
        foreach (var orgUnit in listed)
        {
            var id = StripIdPrefix(orgUnit.OrgUnitId);
            byPath.TryAdd(orgUnit.OrgUnitPath, new OrgUnitNode(id, orgUnit.Name ?? id, orgUnit.OrgUnitPath));
        }

        foreach (var orgUnit in listed)
        {
            if (!byPath.TryGetValue(orgUnit.OrgUnitPath, out var node) || ReferenceEquals(node, root))
                continue;
            FindNearest(byPath, orgUnit.ParentOrgUnitPath ?? ParentPath(orgUnit.OrgUnitPath)).Children.Add(node);
        }

        foreach (var user in users)
        {
            var path = string.IsNullOrWhiteSpace(user.OrgUnitPath) ? "/" : user.OrgUnitPath;
            var node = FindNearest(byPath, path);
            if (!string.Equals(node.OrgUnitPath, path, StringComparison.OrdinalIgnoreCase))
                Log.WriteVerboseMessage(LOGTAG, "UserOrgUnitNotFound", Strings.UserOrgUnitNotFound(user.PrimaryEmail, path, node.OrgUnitPath));
            node.Users.Add(user);
        }

        // The Drive API reports the id of the organizational unit, which the Directory API
        // reports with a prefix, so both are compared without it
        var byId = byPath.Values.Where(x => !ReferenceEquals(x, root)).GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        foreach (var drive in sharedDrives)
        {
            var id = string.IsNullOrWhiteSpace(drive.OrgUnitId) ? null : StripIdPrefix(drive.OrgUnitId);
            if (id == null || !byId.TryGetValue(id, out var node))
            {
                if (id != null)
                    Log.WriteVerboseMessage(LOGTAG, "SharedDriveOrgUnitNotFound", Strings.SharedDriveOrgUnitNotFound(drive.Name ?? drive.Id, drive.OrgUnitId!));
                node = root;
            }
            node.SharedDrives.Add(drive);
        }

        return root;
    }

    /// <summary>
    /// Removes the <c>id:</c> prefix the Directory API puts on organizational unit ids
    /// </summary>
    /// <param name="id">The id</param>
    /// <returns>The id without the prefix</returns>
    private static string StripIdPrefix(string id)
        => id.StartsWith(ID_PREFIX, StringComparison.Ordinal) ? id.Substring(ID_PREFIX.Length) : id;

    /// <summary>
    /// Finds the organizational unit with the path, or the nearest ancestor that is known
    /// </summary>
    /// <param name="byPath">The organizational units by path</param>
    /// <param name="path">The path to find</param>
    /// <returns>The organizational unit</returns>
    private static OrgUnitNode FindNearest(Dictionary<string, OrgUnitNode> byPath, string path)
    {
        var current = path;
        while (true)
        {
            if (byPath.TryGetValue(current, out var node))
                return node;
            if (current == "/" || string.IsNullOrEmpty(current))
                return byPath["/"];
            current = ParentPath(current);
        }
    }

    /// <summary>
    /// The path of the parent of an organizational unit path
    /// </summary>
    /// <param name="path">The path</param>
    /// <returns>The parent path</returns>
    private static string ParentPath(string path)
    {
        var index = path.TrimEnd('/').LastIndexOf('/');
        return index <= 0 ? "/" : path.Substring(0, index);
    }

    /// <summary>
    /// The scope the change tracking state of a user in this organizational unit is stored
    /// under. A user moved to another organizational unit gets a new path, so the state stored
    /// for the old path must not be used for it.
    /// </summary>
    /// <param name="userId">The primary email of the user</param>
    /// <returns>The scope</returns>
    public string ChangeTrackingUserScope(string userId)
        => $"{userId}|ou:{Id}";

    /// <summary>
    /// The scope the change tracking state of a shared drive in this organizational unit is
    /// stored under, when the drive is listed at the tenant level. A shared drive moved to
    /// another organizational unit gets a new path, so the state stored for the old path must
    /// not be used for it.
    /// </summary>
    /// <returns>The scope</returns>
    public string ChangeTrackingSharedDriveScope()
        => $"|ou:{Id}";
}

/// <summary>
/// The folder of an organizational unit, holding the folders of the organizational units
/// below it and the folders of the users or shared drives placed in it
/// </summary>
/// <param name="provider">The source provider</param>
/// <param name="parentPath">The path of the folder the organizational unit folder is placed in</param>
/// <param name="seatRootPath">The path of the Users or Shared Drives folder, which the seat count is kept under</param>
/// <param name="node">The organizational unit</param>
internal class OrganizationalUnitFolderSourceEntry(SourceProvider provider, string parentPath, string seatRootPath, OrgUnitNode node)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, node.Id)), null, null)
{
    public override IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
        => EnumerateNode(provider, this.Path, seatRootPath, node, cancellationToken);

    /// <summary>
    /// Lists the content of an organizational unit: its organizational units, then its users
    /// and shared drives. A tree holds either users or shared drives, as each is built for one
    /// of the root folders.
    /// </summary>
    /// <param name="provider">The source provider</param>
    /// <param name="path">The path of the folder of the organizational unit</param>
    /// <param name="seatRootPath">The path of the Users or Shared Drives folder, which the seat count is kept under</param>
    /// <param name="node">The organizational unit</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entries</returns>
    public static async IAsyncEnumerable<ISourceProviderEntry> EnumerateNode(SourceProvider provider, string path, string seatRootPath, OrgUnitNode node, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var child in node.Children)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;
            yield return new OrganizationalUnitFolderSourceEntry(provider, path, seatRootPath, child);
        }

        foreach (var user in node.Users)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (provider.LicenseApprovedForEntry(seatRootPath, GoogleRootType.Users, user.Id, increment: false, countsAsSeat: SourceProvider.UserCountsAsSeat(user)))
                yield return new UserSourceEntry(provider, path, seatRootPath, user, node.ChangeTrackingUserScope(user.PrimaryEmail));
        }

        if (node.SharedDrives.Count > 0)
        {
            var driveService = provider.ApiHelper.GetDriveService();
            foreach (var drive in node.SharedDrives)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                if (provider.LicenseApprovedForEntry(seatRootPath, GoogleRootType.SharedDrives, drive.Id, increment: false, countsAsSeat: true))
                    yield return new SharedDriveSourceEntry(provider, path, seatRootPath, null, false, drive, driveService, node.ChangeTrackingSharedDriveScope());
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "gsuite:v", "1" },
                { "gsuite:Type", SourceItemType.OrganizationalUnitFolder.ToString() },
                { "gsuite:Name", node.Name },
                { "gsuite:Id", node.Id },
                { "gsuite:OrgUnitPath", node.OrgUnitPath }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
}
