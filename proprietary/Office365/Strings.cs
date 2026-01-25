// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Localization.Short;

namespace Duplicati.Proprietary.Office365;

internal static class Strings
{
    public static string ProviderDisplayName => LC.L("Microsoft 365 Provider");

    public static string ProviderDescription => LC.L(
        "Expose Microsoft 365 content through a virtual folder structure organized by content type and internal structure.");

    public static string WebModuleDisplayName => LC.L("Microsoft 365 Web Module");
    public static string WebModuleDescription => LC.L("Provides a web API for managing Microsoft 365 backups.");

    public static string MissingAccessToken => LC.L("An OAuth access token is required for this workspace backend.");
    public static string InvalidRestoreTargetType(string? type) => LC.L($"Invalid restore target type: {type}");

    public static string MetadataStorageNotEnabled(string optionname) => LC.L(
        $"Storing metadata content in the database must be enabled to use the Microsoft 365 source provider. Use the option: --{optionname}");

    public static string RestoreTargetMissingOverwriteOption(string originalOptionName, string alternateOptionName) => LC.L($"The Office365 restore target must have the --{originalOptionName} option set to true. Use the provider specific option --{alternateOptionName} to avoid overwriting existing items.");


    public static string MissingTenantId => LC.L("A tenant ID must be supplied to query the Office 365 Management API.");

    public static string OfficeTenantOptionShort => LC.L("Azure AD tenant identifier.");

    public static string OfficeTenantOptionLong => LC.L("The tenant GUID used when calling the Office 365 Management Activity APIs.");

    public static string OfficeClientOptionShort => LC.L("Azure application (client) ID.");

    public static string OfficeClientOptionLong => LC.L("Client identifier used for OAuth2 client credential flow against the Office 365 Management API.");

    public static string OfficeSecretOptionShort => LC.L("Azure application client secret.");

    public static string OfficeSecretOptionLong => LC.L("Client secret used for OAuth2 client credential flow against the Office 365 Management API.");

    public static string OfficeCertificatePathOptionShort => LC.L("Path to the PKCS12 certificate file.");
    public static string OfficeCertificatePathOptionLong => LC.L("Path to the PKCS12 certificate file used for OAuth2 client credential flow against the Office 365 Management API.");

    public static string OfficeCertificatePasswordOptionShort => LC.L("Password for the certificate file.");
    public static string OfficeCertificatePasswordOptionLong => LC.L("Password for the PKCS12 certificate file used for OAuth2 client credential flow against the Office 365 Management API.");

    public static string OfficeGraphBaseOptionShort => LC.L("Microsoft Graph base URL.");

    public static string OfficeGraphBaseOptionLong => LC.L("Base URL for Microsoft Graph if targeting a sovereign cloud.");

    public static string OfficeIgnoreExistingOptionShort => LC.L("Ignore existing items.");
    public static string OfficeIgnoreExistingOptionLong => LC.L("If set, existing items in the destination will not be overwritten.");

    public static string OfficeScopeOptionShort => LC.L("Microsoft Graph API scope.");
    public static string OfficeScopeOptionLong => LC.L("The scope to use when requesting an access token for the Microsoft Graph API.");

    public static string WebModuleOperationShort => LC.L("The operation to perform.");
    public static string WebModuleOperationLong => LC.L("The operation that the web module should perform.");

    public static string WebModuleURLShort => LC.L("The Microsoft 365 URL.");
    public static string WebModuleURLLong => LC.L("The Microsoft 365 URL to the destination.");

    public static string WebModulePathShort => LC.L("The path within the Microsoft 365 destination.");
    public static string WebModulePathLong => LC.L("The path within the Microsoft 365 destination to list.");

    public static string OfficeIncludedRootTypesShort => LC.L("Included root types.");
    public static string OfficeIncludedRootTypesLong => LC.L("The root types to include in the backup (e.g. Users, Groups, Sites).");

    public static string OfficeIncludedUserTypesShort => LC.L("Included user types.");
    public static string OfficeIncludedUserTypesLong => LC.L("The user types to include in the backup (e.g. Mailbox, OneDrive, Calendar).");

    public static string OfficeIncludedGroupTypesShort => LC.L("Included group types.");
    public static string OfficeIncludedGroupTypesLong => LC.L("The group types to include in the backup (e.g. Mailbox, OneDrive, Calendar).");

}
