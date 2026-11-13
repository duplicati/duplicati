// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using OrgUnit = Google.Apis.Admin.Directory.directory_v1.Data.OrgUnit;
using User = Google.Apis.Admin.Directory.directory_v1.Data.User;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

/// <summary>
/// An organizational unit in the tree the users are listed in when the provider places the
/// user folders by organizational unit
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
    /// The prefix of organizational unit ids in the Directory API
    /// </summary>
    private const string ID_PREFIX = "id:";

    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<OrgUnitNode>();

    /// <summary>
    /// Builds the tree of organizational units and places each user in its organizational
    /// unit. A user whose organizational unit is not in the list, such as one created after
    /// the list was read, is placed in the nearest listed ancestor, or in the root.
    /// </summary>
    /// <param name="orgUnits">The organizational units of the tenant</param>
    /// <param name="users">The users to place</param>
    /// <returns>The root organizational unit</returns>
    public static OrgUnitNode Build(IEnumerable<OrgUnit> orgUnits, IEnumerable<User> users)
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
            var id = orgUnit.OrgUnitId.StartsWith(ID_PREFIX, StringComparison.Ordinal)
                ? orgUnit.OrgUnitId.Substring(ID_PREFIX.Length)
                : orgUnit.OrgUnitId;
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

        return root;
    }

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
}

/// <summary>
/// The folder of an organizational unit, holding the folders of the organizational units
/// below it and the folders of the users placed in it
/// </summary>
internal class OrganizationalUnitFolderSourceEntry(SourceProvider provider, string parentPath, string usersRootPath, OrgUnitNode node)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, node.Id)), null, null)
{
    public override IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
        => EnumerateNode(provider, this.Path, usersRootPath, node, cancellationToken);

    /// <summary>
    /// Lists the content of an organizational unit: its organizational units, then its users
    /// </summary>
    /// <param name="provider">The source provider</param>
    /// <param name="path">The path of the folder of the organizational unit</param>
    /// <param name="usersRootPath">The path of the Users folder, which the seat count is kept under</param>
    /// <param name="node">The organizational unit</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entries</returns>
    public static async IAsyncEnumerable<ISourceProviderEntry> EnumerateNode(SourceProvider provider, string path, string usersRootPath, OrgUnitNode node, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var child in node.Children)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;
            yield return new OrganizationalUnitFolderSourceEntry(provider, path, usersRootPath, child);
        }

        foreach (var user in node.Users)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (provider.LicenseApprovedForEntry(usersRootPath, GoogleRootType.Users, user.Id, increment: false, countsAsSeat: SourceProvider.UserCountsAsSeat(user)))
                yield return new UserSourceEntry(provider, path, usersRootPath, user, node.ChangeTrackingUserScope(user.PrimaryEmail));
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
