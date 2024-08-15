using Duplicati.Library.AutoUpdater;

namespace Duplicati.WebserverCore.Abstractions;

public class UpdateInfo
{
    public required int MinimumCompatibleVersion { get; init; }
    public required string? IncompatibleUpdateUrl { get; init; }
    public required string? Displayname { get; init; }
    public required string? Version { get; init; }
    public required DateTime ReleaseTime { get; init; }
    public required string? ReleaseType { get; init; }
    public required string? UpdateSeverity { get; init; }
    public required string? ChangeInfo { get; init; }
    public required int PackageUpdaterVersion { get; init; }
    public required PackageEntry[]? Packages { get; init; }
    public required string GenericUpdatePageUrl { get; init; }

    public static UpdateInfo? FromSrc(Library.AutoUpdater.UpdateInfo? src)
    {
        if (src == null)
            return null;

        return new UpdateInfo
        {
            MinimumCompatibleVersion = src.MinimumCompatibleVersion,
            IncompatibleUpdateUrl = src.IncompatibleUpdateUrl,
            Displayname = src.Displayname,
            Version = src.Version,
            ReleaseTime = src.ReleaseTime,
            ReleaseType = src.ReleaseType,
            UpdateSeverity = src.UpdateSeverity,
            ChangeInfo = src.ChangeInfo,
            PackageUpdaterVersion = src.PackageUpdaterVersion,
            Packages = src.Packages,
            GenericUpdatePageUrl = src.GenericUpdatePageUrl
        };
    }
}