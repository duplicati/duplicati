// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;

namespace Duplicati.Proprietary.GoogleWorkspace;

[Flags]
public enum GoogleRootType
{
    Users = 1,
    Groups = 2,
    SharedDrives = 4,
    Sites = 8,
    OrganizationalUnits = 16
}

[Flags]
public enum GoogleUserType
{
    Gmail = 1,
    Drive = 2,
    Calendar = 4,
    Contacts = 8,
    Tasks = 16,
    Keep = 32,
    Chat = 64
}

public static class OptionsHelper
{
    public const string ModuleKey = "googleworkspace";

    public const string GOOGLE_CLIENT_ID_OPTION = "google-client-id";
    public const string GOOGLE_CLIENT_SECRET_OPTION = "google-client-secret";
    public const string GOOGLE_REFRESH_TOKEN_OPTION = "google-refresh-token";
    public const string GOOGLE_SERVICE_ACCOUNT_JSON_OPTION = "google-service-account-json";
    public const string GOOGLE_SERVICE_ACCOUNT_FILE_OPTION = "google-service-account-file";
    public const string GOOGLE_ADMIN_EMAIL_OPTION = "google-admin-email";
    public const string GOOGLE_INCLUDED_ROOT_TYPES_OPTION = "google-included-root-types";
    public const string GOOGLE_INCLUDED_USER_TYPES_OPTION = "google-included-user-types";
    public const string GOOGLE_IGNORE_EXISTING_OPTION = "google-ignore-existing";
    public const string GOOGLE_AVOID_CALENDAR_ACL_OPTION = "google-ignore-calendar-acl";

    private static readonly ICommandLineArgument[] SupportedCommands =
    [
        new CommandLineArgument(GOOGLE_CLIENT_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.Options.GoogleClientIdShort, Strings.Options.GoogleClientIdLong, null, [AuthOptionsHelper.AuthUsernameOption]),
        new CommandLineArgument(GOOGLE_CLIENT_SECRET_OPTION, CommandLineArgument.ArgumentType.Password, Strings.Options.GoogleClientSecretShort, Strings.Options.GoogleClientSecretLong, null, [AuthOptionsHelper.AuthPasswordOption]),
        new CommandLineArgument(GOOGLE_REFRESH_TOKEN_OPTION, CommandLineArgument.ArgumentType.Password, Strings.Options.GoogleRefreshTokenShort, Strings.Options.GoogleRefreshTokenLong),
        new CommandLineArgument(GOOGLE_SERVICE_ACCOUNT_JSON_OPTION, CommandLineArgument.ArgumentType.Password, Strings.Options.GoogleServiceAccountJsonShort, Strings.Options.GoogleServiceAccountJsonLong),
        new CommandLineArgument(GOOGLE_SERVICE_ACCOUNT_FILE_OPTION, CommandLineArgument.ArgumentType.Path, Strings.Options.GoogleServiceAccountFileShort, Strings.Options.GoogleServiceAccountFileLong),
        new CommandLineArgument(GOOGLE_ADMIN_EMAIL_OPTION, CommandLineArgument.ArgumentType.String, Strings.Options.GoogleAdminEmailShort, Strings.Options.GoogleAdminEmailLong),
    ];

    public static readonly ICommandLineArgument[] RestoreSupportedCommands =
    [
        ..SupportedCommands,
        new CommandLineArgument(GOOGLE_IGNORE_EXISTING_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Options.GoogleIgnoreExistingOptionShort, Strings.Options.GoogleIgnoreExistingOptionLong),
    ];

    public static readonly ICommandLineArgument[] SourceProviderSupportedCommands =
    [
        ..SupportedCommands,
        new CommandLineArgument(GOOGLE_INCLUDED_ROOT_TYPES_OPTION, CommandLineArgument.ArgumentType.Flags, Strings.Options.GoogleIncludedRootTypesShort, Strings.Options.GoogleIncludedRootTypesLong, string.Join(",", Enum.GetValues<GoogleRootType>().Select(n => n.ToString())), null, Enum.GetNames(typeof(GoogleRootType))),
        new CommandLineArgument(GOOGLE_INCLUDED_USER_TYPES_OPTION, CommandLineArgument.ArgumentType.Flags, Strings.Options.GoogleIncludedUserTypesShort, Strings.Options.GoogleIncludedUserTypesLong, string.Join(",", Enum.GetValues<GoogleUserType>().Select(n => n.ToString())), null, Enum.GetNames(typeof(GoogleUserType))),
        new CommandLineArgument(GOOGLE_AVOID_CALENDAR_ACL_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.Options.GoogleAvoidCalendarAclOptionShort, Strings.Options.GoogleAvoidCalendarAclOptionLong),
    ];

    public class GoogleWorkspaceOptions
    {
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? RefreshToken { get; set; }
        public string? ServiceAccountJson { get; set; }
        public string? AdminEmail { get; set; }
        public GoogleRootType[] IncludedRootTypes { get; set; } = Enum.GetValues<GoogleRootType>().ToArray();
        public GoogleUserType[] IncludedUserTypes { get; set; } = Enum.GetValues<GoogleUserType>().ToArray();
    }

    public static GoogleWorkspaceOptions ParseOptions(Dictionary<string, string?> options)
    {
        var result = new GoogleWorkspaceOptions();

        if (options.ContainsKey(GOOGLE_CLIENT_ID_OPTION))
            result.ClientId = options[GOOGLE_CLIENT_ID_OPTION];
        if (options.ContainsKey(GOOGLE_CLIENT_SECRET_OPTION))
            result.ClientSecret = options[GOOGLE_CLIENT_SECRET_OPTION];
        if (options.ContainsKey(GOOGLE_REFRESH_TOKEN_OPTION))
            result.RefreshToken = options[GOOGLE_REFRESH_TOKEN_OPTION];

        if (options.ContainsKey(GOOGLE_SERVICE_ACCOUNT_JSON_OPTION))
            result.ServiceAccountJson = options[GOOGLE_SERVICE_ACCOUNT_JSON_OPTION];

        if (string.IsNullOrEmpty(result.ServiceAccountJson) && options.ContainsKey(GOOGLE_SERVICE_ACCOUNT_FILE_OPTION))
        {
            var path = options[GOOGLE_SERVICE_ACCOUNT_FILE_OPTION];
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                result.ServiceAccountJson = System.IO.File.ReadAllText(path);
            }
        }

        if (options.ContainsKey(GOOGLE_ADMIN_EMAIL_OPTION))
            result.AdminEmail = options[GOOGLE_ADMIN_EMAIL_OPTION];

        var includedRootTypes = Library.Utility.Utility.ParseFlagsOption(options, GOOGLE_INCLUDED_ROOT_TYPES_OPTION, (GoogleRootType)Enum.GetValues<GoogleRootType>().Cast<int>().Aggregate(0, (acc, type) => acc | type));
        var includedUserTypes = Library.Utility.Utility.ParseFlagsOption(options, GOOGLE_INCLUDED_USER_TYPES_OPTION, (GoogleUserType)Enum.GetValues<GoogleUserType>().Cast<int>().Aggregate(0, (acc, type) => acc | type));

        result.IncludedRootTypes = Enum.GetValues<GoogleRootType>().Where(n => includedRootTypes.HasFlag(n)).ToArray();
        result.IncludedUserTypes = Enum.GetValues<GoogleUserType>().Where(n => includedUserTypes.HasFlag(n)).ToArray();

        return result;
    }
}
