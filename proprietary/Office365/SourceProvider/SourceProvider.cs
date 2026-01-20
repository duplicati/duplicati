// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

/// <summary>
/// The source provider for Office365.
/// </summary>
public sealed partial class SourceProvider : ISourceProviderModule, IDisposable
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<SourceProvider>();

    /// <summary>
    /// The general page size for Graph API requests.
    /// </summary>
    private const int GENERAL_PAGE_SIZE = OptionsHelper.GENERAL_PAGE_SIZE;

    /// <summary>
    /// API helper for making requests to the Graph API.
    /// </summary>
    private readonly APIHelper _apiHelper;

    /// <summary>
    /// The mount point for this source provider instance.
    /// </summary>
    private readonly string _mountPoint;

    /// <summary>
    /// The timeout options for the backend
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _timeouts;

    /// <summary>
    /// Cache of already resolved source entries by their path.
    /// </summary>
    private readonly ConcurrentDictionary<string, ISourceProviderEntry> _entryCache = new();

    /// <summary>
    /// Counter for tracking which licensed paths are being enumerated.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _enumerationCounter = new();

    /// <summary>
    /// Whether a license warning has been issued.
    /// </summary>
    private int _licenseWarningIssued = 0;

    /// <summary>
    /// Whether this provider is being used for a restore operation.
    /// </summary>
    internal bool UsedForRestoreOperation { get; set; } = false;

    /// <summary>
    /// User types that require delegated permissions, default disabled.
    /// </summary>
    private static readonly Office365UserType[] DELEGATED_USER_TYPES =
    [
        Office365UserType.Tasks,
        Office365UserType.Notes
    ];

    /// <summary>
    /// Group types that require delegated permissions, default disabled.
    /// </summary>
    private static readonly Office365GroupType[] DELEGATED_GROUP_TYPES =
    [
        Office365GroupType.Calendar
    ];

    /// <summary>
    /// The default included root types.
    /// </summary>
    private static readonly Office365MetaType[] DEFAULT_INCLUDED_ROOT_TYPES =
        Enum.GetValues<Office365MetaType>()
            .ToArray();

    /// <summary>
    /// The default included group types.
    /// </summary>
    private static readonly Office365GroupType[] DEFAULT_INCLUDED_GROUP_TYPES =
        Enum.GetValues<Office365GroupType>()
            .Except(DELEGATED_GROUP_TYPES)
            .ToArray();

    /// <summary>
    /// The default included user types.
    /// </summary>
    private static readonly Office365UserType[] DEFAULT_INCLUDED_USER_TYPES =
    Enum.GetValues<Office365UserType>()
        .Except(DELEGATED_USER_TYPES)
        .ToArray();

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceProvider"/> class.
    /// Only used for loading metadata properties.
    /// </summary>
    public SourceProvider()
    {
        _apiHelper = null!;
        _mountPoint = null!;
        _timeouts = null!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceProvider"/> class.
    /// </summary>
    /// <param name="url">The source provider URL.</param>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="options">The source provider options.</param>
    public SourceProvider(string url, string mountPoint, Dictionary<string, string?> options)
    {
        if (!Library.Utility.Utility.ParseBoolOption(options, "store-metadata-content-in-database"))
            throw new UserInformationException(Strings.MetadataStorageNotEnabled("store-metadata-content-in-database"), "DatabaseMetadataStorageNotEnabled");

        _mountPoint = mountPoint;
        var parsedOptions = OptionsHelper.ParseAndValidateOptions(url, options);
        _timeouts = TimeoutOptionsHelper.Parse(options);
        _apiHelper = APIHelper.Create(
            tenantId: parsedOptions.TenantId,
            authOptions: parsedOptions.AuthOptions,
            graphBaseUrl: parsedOptions.GraphBaseUrl,
            timeouts: _timeouts,
            certificatePath: parsedOptions.CertificatePath,
            certificatePassword: parsedOptions.CertificatePassword,
            scope: parsedOptions.Scope
        );
    }

    /// <inheritdoc />
    public string Key => OptionsHelper.ModuleKey;

    /// <inheritdoc />
    public string DisplayName => Strings.ProviderDisplayName;

    /// <inheritdoc />
    public string Description => Strings.ProviderDescription;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SupportedCommands.Concat(TimeoutOptionsHelper.GetOptions()).ToList();

    /// <inheritdoc />
    public string MountedPath => _mountPoint;

    /// <inheritdoc />
    public void Dispose()
    {
        _apiHelper?.Dispose();
    }


    /// <inheritdoc />
    public Task Initialize(CancellationToken cancellationToken)
        => _apiHelper.AcquireAccessTokenAsync(true, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new RootSourceEntry(this, _mountPoint);
    }

    /// <inheritdoc />
    public async Task<ISourceProviderEntry?> GetEntry(string path, bool isFolder, CancellationToken cancellationToken)
    {
        if (_entryCache.TryGetValue(path, out var cachedEntry))
            return cachedEntry;

        ISourceProviderEntry resultEntry = new RootSourceEntry(this, _mountPoint);
        var targetPath = path.TrimEnd(Path.DirectorySeparatorChar);

        var currentPath = targetPath;
        while (!string.IsNullOrEmpty(currentPath))
        {
            var parentPath = Path.GetDirectoryName(currentPath);
            if (parentPath != null && _entryCache.TryGetValue(parentPath, out var cachedParent))
            {
                resultEntry = cachedParent;
                break;
            }
            currentPath = parentPath;
        }

        var relativePath = string.IsNullOrEmpty(currentPath)
            ? targetPath
            : Path.GetRelativePath(currentPath, targetPath);

        var pathsegments = relativePath?.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();

        var segmentIndex = 0;
        foreach (var item in pathsegments)
        {
            var found = false;
            var isLastSegment = segmentIndex == pathsegments.Length - 1;
            await foreach (var entry in resultEntry.Enumerate(cancellationToken))
            {
                _entryCache.TryAdd(entry.Path, entry);

                var name = entry.Path.TrimEnd(Path.DirectorySeparatorChar)
                    .Split(Path.DirectorySeparatorChar)
                    .Last();
                if (name.Equals(item, StringComparison.OrdinalIgnoreCase))
                {
                    if (isLastSegment && entry.IsFolder != isFolder)
                        throw new UserInformationException($"Path segment '{item}' is not a {(isFolder ? "folder" : "file")}", "PathSegmentNotFolder");
                    else if (!isLastSegment && !entry.IsFolder)
                        throw new UserInformationException($"Path segment '{item}' is not a folder", "PathSegmentNotFolder");

                    resultEntry = entry;
                    found = true;
                    break;
                }
            }

            segmentIndex++;
            if (!found)
                return null;
        }

        return resultEntry;
    }

    /// <summary>
    /// Determines whether the license is approved for the given entry.
    /// </summary>
    /// <param name="path">The path to verify.</param>
    /// <param name="type">The type of entry.</param>
    /// <param name="id">The ID of the entry.</param>
    /// <returns><c>true</c> if the license is approved; otherwise, <c>false</c>.</returns>
    internal bool LicenseApprovedForEntry(string path, Office365MetaType type, string id)
    {
        // We do not limit restores
        if (UsedForRestoreOperation)
            return true;

        // Make a unique target path for the type and id, just for counting purposes, not matching actual paths
        var targetpath = $"{path.TrimEnd(Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}{type}{Path.DirectorySeparatorChar}{id}";

        if (_enumerationCounter.ContainsKey(targetpath))
            return true;

        var approved = LicenseChecker.LicenseHelper.AvailableOffice365FeatureSeats;
        if (_enumerationCounter.Count >= approved)
        {
            if (Interlocked.Exchange(ref _licenseWarningIssued, 1) == 0)
                Library.Logging.Log.WriteWarningMessage(LOGTAG, "LicenseWarning", null, $"Licensed Office 365 feature seats exceeded ({approved}). Some items will not be backed up.");
            return false;
        }

        _enumerationCounter[targetpath] = true;
        return true;
    }


    /// <summary>
    /// Gets the included root types.
    /// </summary>
    internal IEnumerable<Office365MetaType> IncludedRootTypes =>
       DEFAULT_INCLUDED_ROOT_TYPES;

    /// <summary>
    /// Gets the included user types.
    /// </summary>
    internal IEnumerable<Office365UserType> IncludedUserTypes =>
        DEFAULT_INCLUDED_USER_TYPES;

    /// <summary>
    /// Gets the included group types.
    /// </summary>
    internal IEnumerable<Office365GroupType> IncludedGroupTypes =>
        DEFAULT_INCLUDED_GROUP_TYPES;
}
