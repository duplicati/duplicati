// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class RootSourceEntry(SourceProvider provider, string mountPoint)
    : MetaEntryBase(Util.AppendDirSeparator(mountPoint), null, null)
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

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Type", SourceItemType.MetaRoot.ToString() },
                { "o365:Name", "Office 365 Tenant" },
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

        yield return new StreamResourceEntryFunction(SystemIO.IO_OS.PathCombine(this.Path, "office-365-backup-description.json"),
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
                { "ExtType", "o365"},
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

        foreach (var type in provider.IncludedRootTypes)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;
            yield return new MetaRootSourceEntry(provider, Path, type);
        }
    }

    public override Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        => Task.FromResult(Enum.GetValues<Office365MetaType>().Any(x => x.ToString() == filename));
}
