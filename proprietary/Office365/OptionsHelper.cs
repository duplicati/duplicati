using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

internal static class OptionsHelper
{
    internal const string ModuleKey = "office365";
    internal const string OFFICE_TENANT_OPTION = "office365-tenant-id";
    internal const string OFFICE_CLIENT_OPTION = "office365-client-id";
    internal const string OFFICE_SECRET_OPTION = "office365-client-secret";
    internal const string OFFICE_CERTIFICATE_PATH_OPTION = "office365-certificate-path";
    internal const string OFFICE_CERTIFICATE_PASSWORD_OPTION = "office365-certificate-password";
    internal const string OFFICE_GRAPH_BASE_OPTION = "office365-graph-base-url";
    internal const string OFFICE_SCOPE_OPTION = "office365-scope";
    internal const string OFFICE_SCOPE_OPTION_DEFAULT = "https://graph.microsoft.com/.default";
    internal const string OFFICE_IGNORE_EXISTING_OPTION = "office365-ignore-existing";
    internal const string OFFICE_INCLUDED_ROOT_TYPES_OPTION = "office365-included-root-types";
    internal const string OFFICE_INCLUDED_USER_TYPES_OPTION = "office365-included-user-types";
    internal const string OFFICE_INCLUDED_GROUP_TYPES_OPTION = "office365-included-group-types";
    internal const string OFFICE_INCLUDED_USER_CLASSIFICATIONS_OPTION = "office365-included-user-classifications";
    internal const string OFFICE_INCLUDED_GROUP_CLASSIFICATIONS_OPTION = "office365-included-group-classifications";
    internal const string OFFICE_INCLUDED_SITE_CLASSIFICATIONS_OPTION = "office365-included-site-classifications";

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
        Office365GroupType.Calendar,
        Office365GroupType.Notes
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
    /// All user classifications, i.e. the default include filter (everything included).
    /// </summary>
    internal static readonly Office365UserClassification ALL_USER_CLASSIFICATIONS =
        Enum.GetValues<Office365UserClassification>().Aggregate((Office365UserClassification)0, (acc, v) => acc | v);

    /// <summary>
    /// All group classifications, i.e. the default include filter (everything included).
    /// </summary>
    internal static readonly Office365GroupClassification ALL_GROUP_CLASSIFICATIONS =
        Enum.GetValues<Office365GroupClassification>().Aggregate((Office365GroupClassification)0, (acc, v) => acc | v);

    /// <summary>
    /// All site classifications, i.e. the default include filter (everything included).
    /// </summary>
    internal static readonly Office365SiteClassification ALL_SITE_CLASSIFICATIONS =
        Enum.GetValues<Office365SiteClassification>().Aggregate((Office365SiteClassification)0, (acc, v) => acc | v);

    internal const string DEFAULT_GRAPH_BASE_URL = "https://graph.microsoft.com";

    internal sealed record ParsedOptions(
        string TenantId,
        AuthOptionsHelper.AuthOptions AuthOptions,
        string GraphBaseUrl,
        string? CertificatePath,
        string? CertificatePassword,
        string Scope,
        Office365MetaType[] IncludedRootTypes,
        Office365UserType[] IncludedUserTypes,
        Office365GroupType[] IncludedGroupTypes,
        Office365UserClassification IncludedUserClassifications,
        Office365GroupClassification IncludedGroupClassifications,
        Office365SiteClassification IncludedSiteClassifications
    );

    internal static ParsedOptions ParseAndValidateOptions(string url, Dictionary<string, string?> options)
    {
        var _authOptions = AuthOptionsHelper.ParseWithAlias(options, new Library.Utility.CompatUri(url), OFFICE_CLIENT_OPTION, OFFICE_SECRET_OPTION)
            .RequireCredentials();
        var _tenantId = options.GetValueOrDefault(OFFICE_TENANT_OPTION)!;
        if (string.IsNullOrWhiteSpace(_tenantId))
            throw new UserInformationException(Strings.MissingTenantId, "MissingTenantId");

        var _graphBaseUrl = options.GetValueOrDefault(OFFICE_GRAPH_BASE_OPTION) ?? DEFAULT_GRAPH_BASE_URL;
        if (string.IsNullOrWhiteSpace(_graphBaseUrl))
            _graphBaseUrl = DEFAULT_GRAPH_BASE_URL;

        var _certificatePath = options.GetValueOrDefault(OFFICE_CERTIFICATE_PATH_OPTION);
        var _certificatePassword = options.GetValueOrDefault(OFFICE_CERTIFICATE_PASSWORD_OPTION);

        var _scope = options.GetValueOrDefault(OFFICE_SCOPE_OPTION);
        if (string.IsNullOrWhiteSpace(_scope))
            _scope = OFFICE_SCOPE_OPTION_DEFAULT;

        var includedRootTypes = Library.Utility.Utility.ParseFlagsOption(options, OFFICE_INCLUDED_ROOT_TYPES_OPTION, (Office365MetaType)DEFAULT_INCLUDED_ROOT_TYPES.Aggregate(0, (acc, type) => acc | (int)type));
        var includedUserTypes = Library.Utility.Utility.ParseFlagsOption(options, OFFICE_INCLUDED_USER_TYPES_OPTION, (Office365UserType)DEFAULT_INCLUDED_USER_TYPES.Aggregate(0, (acc, type) => acc | (int)type));
        var includedGroupTypes = Library.Utility.Utility.ParseFlagsOption(options, OFFICE_INCLUDED_GROUP_TYPES_OPTION, (Office365GroupType)DEFAULT_INCLUDED_GROUP_TYPES.Aggregate(0, (acc, type) => acc | (int)type));

        // Classification include filters default to all classifications included.
        var includedUserClassifications = Library.Utility.Utility.ParseFlagsOption(options, OFFICE_INCLUDED_USER_CLASSIFICATIONS_OPTION, ALL_USER_CLASSIFICATIONS);
        var includedGroupClassifications = Library.Utility.Utility.ParseFlagsOption(options, OFFICE_INCLUDED_GROUP_CLASSIFICATIONS_OPTION, ALL_GROUP_CLASSIFICATIONS);
        var includedSiteClassifications = Library.Utility.Utility.ParseFlagsOption(options, OFFICE_INCLUDED_SITE_CLASSIFICATIONS_OPTION, ALL_SITE_CLASSIFICATIONS);

        return new ParsedOptions(
            TenantId: _tenantId,
            AuthOptions: _authOptions,
            GraphBaseUrl: _graphBaseUrl,
            CertificatePath: _certificatePath,
            CertificatePassword: _certificatePassword,
            Scope: _scope,
            IncludedRootTypes: Enum.GetValues<Office365MetaType>().Where(n => includedRootTypes.HasFlag(n)).ToArray(),
            IncludedUserTypes: Enum.GetValues<Office365UserType>().Where(n => includedUserTypes.HasFlag(n)).ToArray(),
            IncludedGroupTypes: Enum.GetValues<Office365GroupType>().Where(n => includedGroupTypes.HasFlag(n)).ToArray(),
            IncludedUserClassifications: includedUserClassifications,
            IncludedGroupClassifications: includedGroupClassifications,
            IncludedSiteClassifications: includedSiteClassifications
        );
    }

    internal static IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(OFFICE_TENANT_OPTION, CommandLineArgument.ArgumentType.String, Strings.OfficeTenantOptionShort, Strings.OfficeTenantOptionLong),
        new CommandLineArgument(OFFICE_CLIENT_OPTION, CommandLineArgument.ArgumentType.String, Strings.OfficeClientOptionShort, Strings.OfficeClientOptionLong, null, [AuthOptionsHelper.AuthUsernameOption], null),
        new CommandLineArgument(OFFICE_SECRET_OPTION, CommandLineArgument.ArgumentType.Password, Strings.OfficeSecretOptionShort, Strings.OfficeSecretOptionLong, null, [AuthOptionsHelper.AuthPasswordOption], null),
        new CommandLineArgument(OFFICE_CERTIFICATE_PATH_OPTION, CommandLineArgument.ArgumentType.Path, Strings.OfficeCertificatePathOptionShort, Strings.OfficeCertificatePathOptionLong),
        new CommandLineArgument(OFFICE_CERTIFICATE_PASSWORD_OPTION, CommandLineArgument.ArgumentType.Password, Strings.OfficeCertificatePasswordOptionShort, Strings.OfficeCertificatePasswordOptionLong),
        new CommandLineArgument(OFFICE_GRAPH_BASE_OPTION, CommandLineArgument.ArgumentType.String, Strings.OfficeGraphBaseOptionShort, Strings.OfficeGraphBaseOptionLong, DEFAULT_GRAPH_BASE_URL),
        new CommandLineArgument(OFFICE_SCOPE_OPTION, CommandLineArgument.ArgumentType.String, Strings.OfficeScopeOptionShort, Strings.OfficeScopeOptionLong, OFFICE_SCOPE_OPTION_DEFAULT),
        new CommandLineArgument(OFFICE_INCLUDED_ROOT_TYPES_OPTION, CommandLineArgument.ArgumentType.Flags, Strings.OfficeIncludedRootTypesShort, Strings.OfficeIncludedRootTypesLong, string.Join(",", DEFAULT_INCLUDED_ROOT_TYPES.Select(n => n.ToString()).ToArray()), null, Enum.GetNames<Office365MetaType>()),
        new CommandLineArgument(OFFICE_INCLUDED_USER_TYPES_OPTION, CommandLineArgument.ArgumentType.Flags, Strings.OfficeIncludedUserTypesShort, Strings.OfficeIncludedUserTypesLong, string.Join(",", DEFAULT_INCLUDED_USER_TYPES.Select(n => n.ToString()).ToArray()), null, Enum.GetNames<Office365UserType>()),
        new CommandLineArgument(OFFICE_INCLUDED_GROUP_TYPES_OPTION, CommandLineArgument.ArgumentType.Flags, Strings.OfficeIncludedGroupTypesShort, Strings.OfficeIncludedGroupTypesLong, string.Join(",", DEFAULT_INCLUDED_GROUP_TYPES.Select(n => n.ToString()).ToArray()), null, Enum.GetNames<Office365GroupType>()),
        new CommandLineArgument(OFFICE_INCLUDED_USER_CLASSIFICATIONS_OPTION, CommandLineArgument.ArgumentType.Flags, Strings.OfficeIncludedUserClassificationsShort, Strings.OfficeIncludedUserClassificationsLong, string.Join(",", Enum.GetNames<Office365UserClassification>()), null, Enum.GetNames<Office365UserClassification>()),
        new CommandLineArgument(OFFICE_INCLUDED_GROUP_CLASSIFICATIONS_OPTION, CommandLineArgument.ArgumentType.Flags, Strings.OfficeIncludedGroupClassificationsShort, Strings.OfficeIncludedGroupClassificationsLong, string.Join(",", Enum.GetNames<Office365GroupClassification>()), null, Enum.GetNames<Office365GroupClassification>()),
        new CommandLineArgument(OFFICE_INCLUDED_SITE_CLASSIFICATIONS_OPTION, CommandLineArgument.ArgumentType.Flags, Strings.OfficeIncludedSiteClassificationsShort, Strings.OfficeIncludedSiteClassificationsLong, string.Join(",", Enum.GetNames<Office365SiteClassification>()), null, Enum.GetNames<Office365SiteClassification>())
    ];
}