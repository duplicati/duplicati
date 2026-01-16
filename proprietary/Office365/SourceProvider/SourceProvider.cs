// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

public sealed partial class SourceProvider : ISourceProviderModule, IDisposable
{
    private const int GENERAL_PAGE_SIZE = OptionsHelper.GENERAL_PAGE_SIZE;

    private readonly APIHelper _apiHelper;
    private readonly string _mountPoint;

    private readonly ConcurrentDictionary<string, ISourceProviderEntry> _entryCache = new();

    private static readonly Office365UserType[] DELEGATED_USER_TYPES =
    [
        Office365UserType.Tasks,
        Office365UserType.Notes
    ];

    private static readonly Office365GroupType[] DELEGATED_GROUP_TYPES =
    [
        Office365GroupType.Calendar
    ];

    private static readonly Office365MetaType[] DEFAULT_INCLUDED_ROOT_TYPES =
        Enum.GetValues<Office365MetaType>()
            .ToArray();

    private static readonly Office365GroupType[] DEFAULT_INCLUDED_GROUP_TYPES =
        Enum.GetValues<Office365GroupType>()
            .Except(DELEGATED_GROUP_TYPES)
            .ToArray();

    private static readonly Office365UserType[] DEFAULT_INCLUDED_USER_TYPES =
    Enum.GetValues<Office365UserType>()
        .Except(DELEGATED_USER_TYPES)
        .ToArray();

    public SourceProvider()
    {
        _apiHelper = null!;
        _mountPoint = null!;
    }

    public SourceProvider(string url, string mountPoint, Dictionary<string, string?> options)
    {
        if (!Library.Utility.Utility.ParseBoolOption(options, "store-metadata-content-in-database"))
            throw new UserInformationException(Strings.MetadataStorageNotEnabled("store-metadata-content-in-database"), "DatabaseMetadataStorageNotEnabled");

        _mountPoint = mountPoint;
        var parsedOptions = OptionsHelper.ParseAndValidateOptions(url, options);
        _apiHelper = APIHelper.Create(
            tenantId: parsedOptions.TenantId,
            authOptions: parsedOptions.AuthOptions,
            graphBaseUrl: parsedOptions.GraphBaseUrl
        );
    }

    public string Key => OptionsHelper.ModuleKey;

    public string DisplayName => Strings.ProviderDisplayName;

    public string Description => Strings.ProviderDescription;

    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SupportedCommands;

    public string MountedPath => _mountPoint;

    public void Dispose()
    {
        _apiHelper?.Dispose();
    }


    public Task Initialize(CancellationToken cancellationToken)
        => _apiHelper.AcquireAccessTokenAsync(true, cancellationToken);

    public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new RootSourceEntry(this, _mountPoint);
    }

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

        var pathsegments = relativePath?.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        if (pathsegments.Length == 0)
            pathsegments = [""];

        foreach (var item in pathsegments)
        {
            var found = false;
            await foreach (var entry in resultEntry.Enumerate(cancellationToken))
            {
                _entryCache.TryAdd(entry.Path, entry);

                var name = entry.Path.TrimEnd(Path.DirectorySeparatorChar)
                    .Split(Path.DirectorySeparatorChar)
                    .Last();
                if (name.Equals(item, StringComparison.OrdinalIgnoreCase))
                {
                    if (!entry.IsFolder)
                        throw new UserInformationException($"Path segment '{item}' is not a folder", "PathSegmentNotFolder");

                    resultEntry = entry;
                    found = true;
                    break;
                }
            }

            if (!found)
                return null;
        }

        return resultEntry;
    }

    internal IEnumerable<Office365MetaType> IncludedRootTypes =>
       DEFAULT_INCLUDED_ROOT_TYPES;

    internal IEnumerable<Office365UserType> IncludedUserTypes =>
        DEFAULT_INCLUDED_USER_TYPES;


    internal IEnumerable<Office365GroupType> IncludedGroupTypes =>
        DEFAULT_INCLUDED_GROUP_TYPES;
}
