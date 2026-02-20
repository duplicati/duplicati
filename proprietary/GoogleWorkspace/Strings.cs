// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Localization.Short;

namespace Duplicati.Proprietary.GoogleWorkspace;

internal static class Strings
{
    internal static class Options
    {
        public static string GoogleClientIdShort => LC.L("The Google Client ID");
        public static string GoogleClientIdLong => LC.L("The Client ID for the Google App.");
        public static string GoogleClientSecretShort => LC.L("The Google Client Secret");
        public static string GoogleClientSecretLong => LC.L("The Client Secret for the Google App.");
        public static string GoogleRefreshTokenShort => LC.L("The Google Refresh Token");
        public static string GoogleRefreshTokenLong => LC.L("The Refresh Token for the Google App.");
        public static string GoogleServiceAccountJsonShort => LC.L("The Google Service Account JSON");
        public static string GoogleServiceAccountJsonLong => LC.L("The Google Service Account JSON content.");
        public static string GoogleServiceAccountFileShort => LC.L("The Google Service Account File");
        public static string GoogleServiceAccountFileLong => LC.L("The path to the Google Service Account JSON file.");
        public static string GoogleAdminEmailShort => LC.L("The Google Admin Email");
        public static string GoogleAdminEmailLong => LC.L("The email of the Google Admin user to impersonate.");
        public static string GoogleIncludedRootTypesShort => LC.L("Included Root Types");
        public static string GoogleIncludedRootTypesLong => LC.L("Comma-separated list of root types to include (e.g. Users, Groups, SharedDrives).");
        public static string GoogleIncludedUserTypesShort => LC.L("Included User Types");
        public static string GoogleIncludedUserTypesLong => LC.L("Comma-separated list of user types to include (e.g. Gmail, Drive, Calendar).");
        public static string GoogleIgnoreExistingOptionShort => LC.L("Ignore existing items");
        public static string GoogleIgnoreExistingOptionLong => LC.L("If set, existing items in the destination will not be overwritten.");
        public static string GoogleAvoidCalendarAclOptionShort => LC.L("Avoid reading calendar ACLs");
        public static string GoogleAvoidCalendarAclOptionLong => LC.L("Calendar ACLs require write permissions to be able to read them. If the account does not have write permissions, this option can be used to avoid reading them.");
    }

    internal static class Common
    {
        public static string DisplayName => LC.L("Google Workspace");
        public static string Description => LC.L("Backup and restore Google Workspace data.");
        public static string WebModuleDisplayName => LC.L("Google Workspace Web Module");
        public static string WebModuleDescription => LC.L("Web module for Google Workspace.");
        public static string WebModuleOperationShort => LC.L("Operation");
        public static string WebModuleOperationLong => LC.L("The operation to perform.");
        public static string WebModuleURLShort => LC.L("URL");
        public static string WebModuleURLLong => LC.L("The URL to connect to.");
        public static string WebModulePathShort => LC.L("Path");
        public static string WebModulePathLong => LC.L("The path to list.");
    }

    internal static class CalendarAclSourceEntry
    {
        public static string ForbiddenAccessError(string optionname, string scopename) => LC.L($"Access to calendar ACLs was denied. Use the --{optionname} option to avoid reading them or grant the scope {scopename}.");
    }

    public static string MetadataStorageNotEnabled(string optionname) => LC.L(
        $"Storing metadata content in the database must be enabled to use the Google Workspace source provider. Use the option: --{optionname}");

    public static string RestoreTargetMissingOverwriteOption(string originalOptionName, string alternateOptionName) => LC.L($"The Google Workspace restore target must have the --{originalOptionName} option set to true. Use the provider specific option --{alternateOptionName} to avoid overwriting existing items.");

    public static string InvalidRestoreTargetType(string? type) => LC.L($"Invalid restore target type: {type}");
    public static string RestoreTargetNotFound(string path) => LC.L($"Restore target path not found: {path}");
}
