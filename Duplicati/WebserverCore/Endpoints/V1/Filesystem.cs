using Duplicati.Library.Common.IO;
using Duplicati.Library.Snapshots;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Filesystem : IEndpointV1
{
    private record FilesystemInput(string path);
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/filesystem", ([FromQuery] bool? onlyFolders, [FromQuery] bool? showHidden, [FromBody] FilesystemInput input)
            => Execute(input.path, onlyFolders ?? false, showHidden ?? false))
            .RequireAuthorization();

        group.MapPost("/filesystem/validate", ([FromBody] FilesystemInput input)
            => Validate(input.path))
            .RequireAuthorization();
    }

    private static string ExpandPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new BadRequestException("No path was found");

        path = SpecialFolders.ExpandEnvironmentVariables(path);

        if (!OperatingSystem.IsWindows() && !path.StartsWith("/", StringComparison.Ordinal))
            throw new BadRequestException("The path must start with a forward-slash");

        return path;
    }

    private static void Validate(string path)
    {
        path = ExpandPath(path);

        try
        {
            if (Path.IsPathRooted(path) && (Directory.Exists(path) || File.Exists(path)))
                return;
        }
        catch
        {
            throw new NotFoundException("The path does not exist");
        }

    }

    private static IEnumerable<Dto.TreeNodeDto> Execute(string path, bool onlyFolders, bool showHidden)
    {
        if (string.IsNullOrEmpty(path))
            throw new BadRequestException("No path was found");

        string? specialpath = null;
        string? specialtoken = null;

        if (path.StartsWith("%", StringComparison.Ordinal))
        {
            var ix = path.IndexOf("%", 1, StringComparison.Ordinal);
            if (ix > 0)
            {
                var tk = path.Substring(0, ix + 1);
                var node = SpecialFolders.Nodes.FirstOrDefault(x => x.id.Equals(tk, StringComparison.OrdinalIgnoreCase));
                if (node != null)
                {
                    specialpath = node.resolvedpath;
                    specialtoken = node.id;
                }
            }
        }

        path = ExpandPath(path);

        try
        {
            if (path != "" && path != "/")
                path = Util.AppendDirSeparator(path);

            IEnumerable<Dto.TreeNodeDto> res;

            if (OperatingSystem.IsWindows() && (path.Equals("/") || path.Equals("")))
            {
                res = DriveInfo.GetDrives()
                        .Where(di =>
                            (di.DriveType == DriveType.Fixed || di.DriveType == DriveType.Network || di.DriveType == DriveType.Removable)
                            && di.IsReady // Only try to create TreeNode entries for drives who were ready 'now'
                        )
                        .Select(TryCreateTreeNodeForDrive) // This will try to create a TreeNode for selected drives
                        .Where(tn => tn != null) // This filters out such entries that could not be created
                        .Select(x => x!);
            }
            else
            {
                res = ListFolderAsNodes(path, onlyFolders, showHidden);
            }

            if ((path.Equals("/") || path.Equals("")) && specialtoken == null)
            {
                // Prepend special folders
                res = SpecialFolders.Nodes
                    .Select(x => new Dto.TreeNodeDto()
                    {
                        id = x.id,
                        text = x.text,
                        iconCls = x.iconCls,
                        cls = "folder",
                        leaf = false,
                        hidden = false,
                        symlink = false,
                        temporary = false,
                        systemFile = false,
                        fileSize = -1,
                        resolvedpath = x.resolvedpath,
                        check = false

                    })
                    .Union(res);
            }

            if (specialtoken != null && specialpath != null)
            {
                res = res.Select(x => new Dto.TreeNodeDto()
                {
                    id = specialtoken + x.id.Substring(specialpath.Length),
                    text = x.text,
                    iconCls = x.iconCls,
                    cls = "folder",
                    leaf = false,
                    hidden = false,
                    symlink = false,
                    temporary = false,
                    systemFile = false,
                    fileSize = -1,
                    resolvedpath = x.id,
                    check = false
                });
            }

            return res.ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to process the path: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Try to create a new TreeNode instance for the given DriveInfo instance.
    ///
    /// <remarks>
    /// If an exception occurs during creation (most likely the device became unavailable), a null is returned instead.
    /// </remarks>
    /// </summary>
    /// <param name="driveInfo">DriveInfo to try create a TreeNode for. Cannot be null.</param>
    /// <returns>A new TreeNode instance on success; null if an exception occurred during creation.</returns>
    private static Dto.TreeNodeDto? TryCreateTreeNodeForDrive(DriveInfo driveInfo)
    {
        if (driveInfo == null)
            throw new ArgumentNullException(nameof(driveInfo));

        try
        {
            // Try to create the TreeNode
            // This may still fail as the drive might become unavailable in the meanwhile
            return new Dto.TreeNodeDto()
            {
                id = driveInfo.RootDirectory.FullName,
                text =
                (
                    string.IsNullOrWhiteSpace(driveInfo.VolumeLabel)
                        ? driveInfo.RootDirectory.FullName.Replace('\\', ' ')
                        : driveInfo.VolumeLabel + " - " + driveInfo.RootDirectory.FullName.Replace('\\', ' ')
                    ) + "(" + driveInfo.DriveType + ")",
                iconCls = "x-tree-icon-drive",
                cls = "folder",
                leaf = false,
                hidden = false,
                symlink = false,
                temporary = false,
                systemFile = false,
                fileSize = -1,
                resolvedpath = driveInfo.RootDirectory.FullName,
                check = false
            };
        }
        catch
        {
            // Drive became unavailable in the meanwhile or another exception occurred
            // Return a null as fall back
            return null;
        }
    }

    private static IEnumerable<Dto.TreeNodeDto> ListFolderAsNodes(string entrypath, bool skipFiles, bool showHidden)
    {
        //Helper function for finding out if a folder has sub elements
        Func<string, bool> hasSubElements = (p) => skipFiles ? Directory.EnumerateDirectories(p).Any() : Directory.EnumerateFileSystemEntries(p).Any();

        //Helper function for dealing with exceptions when accessing off-limits folders
        Func<string, bool> isEmptyFolder = (p) =>
        {
            try { return !hasSubElements(p); }
            catch { }
            return true;
        };

        //Helper function for dealing with exceptions when accessing off-limits folders
        Func<string, bool> canAccess = (p) =>
        {
            try { hasSubElements(p); return true; }
            catch { }
            return false;
        };

        Func<string, long> getFileSize = (p) =>
        {
            try { return new FileInfo(p).Length; }
            catch { }
            return -1;
        };

        foreach (var s in SystemIO.IO_OS.EnumerateFileSystemEntries(entrypath)
            // Group directories first
            .OrderByDescending(f => SystemIO.IO_OS.GetFileAttributes(f) & FileAttributes.Directory)
            // Sort both groups (directories and files) alphabetically
            .ThenBy(f => f))
        {
            Dto.TreeNodeDto? tn = null;
            try
            {
                var attr = SystemIO.IO_OS.GetFileAttributes(s);
                var isSymlink = SystemIO.IO_OS.IsSymlink(s, attr);
                var isFolder = (attr & FileAttributes.Directory) != 0;
                var isFile = !isFolder;
                var isHidden = (attr & FileAttributes.Hidden) != 0;
                bool isSystem = (attr & FileAttributes.System) != 0;
                bool isTemporary = (attr & FileAttributes.Temporary) != 0;
                long fileSize = -1;

                var accessible = isFile || canAccess(s);
                var isLeaf = isFile || !accessible || isEmptyFolder(s);

                var rawid = isFolder ? Util.AppendDirSeparator(s) : s;
                if (skipFiles && !isFolder)
                    continue;

                if (!showHidden && isHidden)
                    continue;

                if (isFile)
                {
                    fileSize = getFileSize(s);
                }

                tn = new Dto.TreeNodeDto()
                {
                    id = rawid,
                    text = SystemIO.IO_OS.PathGetFileName(s),
                    hidden = isHidden,
                    symlink = isSymlink,
                    temporary = isTemporary,
                    systemFile = isSystem,
                    fileSize = fileSize,
                    iconCls = isFolder ? (accessible ? (isSymlink ? "x-tree-icon-symlink" : "x-tree-icon-parent") : "x-tree-icon-locked") : "x-tree-icon-leaf",
                    leaf = isLeaf,
                    cls = isFolder ? "folder" : "file",
                    check = false,
                    resolvedpath = isSymlink ? SystemIO.IO_OS.GetSymlinkTarget(s) : null
                };
            }
            catch
            {
            }

            if (tn != null)
                yield return tn;
        }
    }
}
