// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

public sealed partial class SourceProvider : ISourceProviderModule, IDisposable
{
    private const int GENERAL_PAGE_SIZE = OptionsHelper.GENERAL_PAGE_SIZE;

    private readonly APIHelper _apiHelper;
    private readonly string _mountPoint;

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
            throw new UserInformationException(Strings.MetadataStorageNotEnabled, "DatabaseMetadataStorageNotEnabled");

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

    public Task<ISourceProviderEntry?> GetEntry(string path, bool isFolder, CancellationToken cancellationToken)
    {
        // TODO: Implement this
        throw new NotImplementedException();
    }

    internal IEnumerable<Office365MetaType> IncludedRootTypes =>
       DEFAULT_INCLUDED_ROOT_TYPES;

    internal IEnumerable<Office365UserType> IncludedUserTypes =>
        DEFAULT_INCLUDED_USER_TYPES;


    internal IEnumerable<Office365GroupType> IncludedGroupTypes =>
        DEFAULT_INCLUDED_GROUP_TYPES;
}
