using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;

namespace Duplicati.Proprietary.Office365;

internal static class OptionsHelper
{
    internal const string ModuleKey = "office365";
    internal const string OFFICE_TENANT_OPTION = "office-tenant-id";
    internal const string OFFICE_CLIENT_OPTION = "office-client-id";
    internal const string OFFICE_SECRET_OPTION = "office-client-secret";
    internal const string OFFICE_CERTIFICATE_PATH_OPTION = "office-certificate-path";
    internal const string OFFICE_CERTIFICATE_PASSWORD_OPTION = "office-certificate-password";
    internal const string OFFICE_GRAPH_BASE_OPTION = "office-graph-base-url";
    internal const string OFFICE_SCOPE_OPTION = "office-scope";
    internal const string OFFICE_SCOPE_OPTION_DEFAULT = "https://graph.microsoft.com/.default";
    internal const string OFFICE_IGNORE_EXISTING_OPTION = "office-ignore-existing";

    internal const string DEFAULT_GRAPH_BASE_URL = "https://graph.microsoft.com";

    internal const int GENERAL_PAGE_SIZE = 100;
    internal const int CHATS_PAGE_SIZE = 50;

    internal sealed record ParsedOptions(
        string TenantId,
        AuthOptionsHelper.AuthOptions AuthOptions,
        string GraphBaseUrl,
        string? CertificatePath,
        string? CertificatePassword,
        string Scope
    );

    internal static ParsedOptions ParseAndValidateOptions(string url, Dictionary<string, string?> options)
    {
        var _authOptions = AuthOptionsHelper.ParseWithAlias(options, new Library.Utility.Uri(url), OFFICE_CLIENT_OPTION, OFFICE_SECRET_OPTION)
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

        return new ParsedOptions(
            TenantId: _tenantId,
            AuthOptions: _authOptions,
            GraphBaseUrl: _graphBaseUrl,
            CertificatePath: _certificatePath,
            CertificatePassword: _certificatePassword,
            Scope: _scope
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
        new CommandLineArgument(OFFICE_SCOPE_OPTION, CommandLineArgument.ArgumentType.String, Strings.OfficeScopeOptionShort, Strings.OfficeScopeOptionLong, OFFICE_SCOPE_OPTION_DEFAULT)
    ];
}