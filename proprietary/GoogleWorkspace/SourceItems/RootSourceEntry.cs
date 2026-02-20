// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.GoogleWorkspace.SourceItems;

internal class RootSourceEntry(SourceProvider provider)
    : MetaEntryBase(Util.AppendDirSeparator(provider.MountedPath), null, null)
{
    internal sealed record BackupDescription(
        string Version,
        string DuplicatiVersion,
        string MachineId,
        string PackageTypeId,
        string OSType,
        string OSVersion,
        string DirectorySeparator,
        string MountPoint
    );

    public override bool IsRootEntry => true;

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "gsuite:v", "1" },
                { "gsuite:Type", SourceItemType.MetaRoot.ToString() },
                { "gsuite:Name", "Google Workspace Tenant" },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var description = new BackupDescription(
            Version: "1.0",
            DuplicatiVersion: Library.AutoUpdater.UpdaterManager.SelfVersion.Version ?? "",
            MachineId: Library.AutoUpdater.DataFolderManager.MachineID,
            PackageTypeId: Library.AutoUpdater.UpdaterManager.PackageTypeId,
            OSType: Library.AutoUpdater.UpdaterManager.OperatingSystemName,
            OSVersion: Library.Utility.OSInfoHelper.PlatformString,
            DirectorySeparator: System.IO.Path.DirectorySeparatorChar.ToString(),
            MountPoint: Path
        );

        if (cancellationToken.IsCancellationRequested)
            yield break;

        yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "google-workspace-backup-description.json"),
            createdUtc: DateTime.UnixEpoch,
            lastModificationUtc: DateTime.UnixEpoch,
            size: -1,
            streamFactory: async (ct) =>
            {
                var ms = new MemoryStream();
                await System.Text.Json.JsonSerializer.SerializeAsync(ms, description, cancellationToken: ct).ConfigureAwait(false);
                ms.Seek(0, SeekOrigin.Begin);
                return ms;
            },
            minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
            {
                { "ExtType", "gsuite"},
                { "Ext:DuplicatiVersion", description.DuplicatiVersion },
                { "Ext:MachineId", description.MachineId },
                { "Ext:PackageTypeId", description.PackageTypeId },
                { "Ext:OSType", description.OSType },
                { "Ext:OSVersion", description.OSVersion },
                { "Ext:DirectorySeparator", description.DirectorySeparator },
                { "Ext:MountPoint", description.MountPoint }
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value))
        );

        if (cancellationToken.IsCancellationRequested)
            yield break;

        foreach (var type in provider.Options.IncludedRootTypes)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (type == GoogleRootType.Users)
            {
                yield return new MetaRootSourceEntry(provider, this.Path, "Users", SourceItemType.MetaRootUsers);
            }
            else if (type == GoogleRootType.Groups)
            {
                yield return new MetaRootSourceEntry(provider, this.Path, "Groups", SourceItemType.MetaRootGroups);
            }
            else if (type == GoogleRootType.SharedDrives)
            {
                yield return new MetaRootSourceEntry(provider, this.Path, "Shared Drives", SourceItemType.MetaRootSharedDrives);
            }
            else if (type == GoogleRootType.Sites)
            {
                yield return new MetaRootSourceEntry(provider, this.Path, "Sites", SourceItemType.MetaRootSites);
            }
            else if (type == GoogleRootType.OrganizationalUnits)
            {
                yield return new MetaRootSourceEntry(provider, this.Path, "Organizational Units", SourceItemType.MetaRootOrganizationalUnits);
            }
        }
    }
}
