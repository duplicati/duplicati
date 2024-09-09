using Duplicati.Server.Serialization.Interface;

namespace Duplicati.WebserverCore.Dto;

public sealed record SystemInfoDto
{
    /// <summary>
    /// Gets or sets the API version.
    /// </summary>
    public required int APIVersion { get; init; }

    /// <summary>
    /// Gets or sets the password placeholder.
    /// </summary>
    public required string PasswordPlaceholder { get; init; }

    /// <summary>
    /// Gets or sets the server version.
    /// </summary>
    public required string? ServerVersion { get; init; }

    /// <summary>
    /// Gets or sets the server version name.
    /// </summary>
    public required string ServerVersionName { get; init; }

    /// <summary>
    /// Gets or sets the server version type.
    /// </summary>
    public required string? ServerVersionType { get; init; }

    /// <summary>
    /// Gets or sets the started by.
    /// </summary>
    public required string StartedBy { get; init; }

    /// <summary>
    /// Gets or sets the default update channel.
    /// </summary>
    public required string DefaultUpdateChannel { get; init; }

    /// <summary>
    /// Gets or sets the default usage report level.
    /// </summary>
    public required string DefaultUsageReportLevel { get; init; }

    /// <summary>
    /// Gets or sets the server time.
    /// </summary>
    public required DateTime ServerTime { get; init; }

    /// <summary>
    /// Gets or sets the OS type.
    /// </summary>
    public required string OSType { get; init; }

    /// <summary>
    /// Gets or sets the directory separator.
    /// </summary>
    public required char DirectorySeparator { get; init; }

    /// <summary>
    /// Gets or sets the path separator.
    /// </summary>
    public required char PathSeparator { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the filesystem is case sensitive.
    /// </summary>
    public required bool CaseSensitiveFilesystem { get; init; }

    /// <summary>
    /// Gets or sets the machine name.
    /// </summary>
    public required string MachineName { get; init; }

    /// <summary>
    /// Gets or sets the package type ID.
    /// </summary>
    public required string PackageTypeId { get; init; }

    /// <summary>
    /// Gets or sets the user name.
    /// </summary>
    public required string UserName { get; init; }

    /// <summary>
    /// Gets or sets the new line character.
    /// </summary>
    public required string NewLine { get; init; }

    /// <summary>
    /// Gets or sets the CLR version.
    /// </summary>
    public required string CLRVersion { get; init; }

    /// <summary>
    /// Gets or sets the CLR OS information.
    /// </summary>
    public required CLROSInfoDto CLROSInfo { get; init; }

    /// <summary>
    /// Gets or sets the options.
    /// </summary>
    public required IEnumerable<Library.Interface.ICommandLineArgument> Options { get; init; }

    /// <summary>
    /// Gets or sets the compression modules.
    /// </summary>
    public required IEnumerable<IDynamicModule> CompressionModules { get; init; }

    /// <summary>
    /// Gets or sets the encryption modules.
    /// </summary>
    public required IEnumerable<IDynamicModule> EncryptionModules { get; init; }

    /// <summary>
    /// Gets or sets the backend modules.
    /// </summary>
    public required IEnumerable<IDynamicModule> BackendModules { get; init; }

    /// <summary>
    /// Gets or sets the generic modules.
    /// </summary>
    public required IEnumerable<IDynamicModule> GenericModules { get; init; }

    /// <summary>
    /// Gets or sets the web modules.
    /// </summary>
    public required IEnumerable<IDynamicModule> WebModules { get; init; }

    /// <summary>
    /// Gets or sets the connection modules.
    /// </summary>
    public required IEnumerable<IDynamicModule> ConnectionModules { get; init; }

    /// <summary>
    /// Gets or sets the server modules.
    /// </summary>
    public required IEnumerable<object> ServerModules { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether alternate update URLs are being used.
    /// </summary>
    public required bool UsingAlternateUpdateURLs { get; init; }

    /// <summary>
    /// Gets or sets the log levels.
    /// </summary>
    public required string[] LogLevels { get; init; }

    /// <summary>
    /// Gets or sets the special folders.
    /// </summary>
    public required IEnumerable<SpecialFolderDto> SpecialFolders { get; init; }

    /// <summary>
    /// Gets or sets the browser locale.
    /// </summary>
    public required LocaleDto BrowserLocale { get; init; }

    /// <summary>
    /// Gets or sets the supported locales.
    /// </summary>
    public required IEnumerable<LocaleDto> SupportedLocales { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the browser locale is supported.
    /// </summary>
    public required bool BrowserLocaleSupported { get; init; }

    /// <summary>
    /// Represents CLR OS information.
    /// </summary>
    public class CLROSInfoDto
    {
        /// <summary>
        /// Gets or sets the platform.
        /// </summary>
        public required string Platform { get; init; }

        /// <summary>
        /// Gets or sets the service pack.
        /// </summary>
        public required string ServicePack { get; init; }

        /// <summary>
        /// Gets or sets the version.
        /// </summary>
        public required string Version { get; init; }

        /// <summary>
        /// Gets or sets the version string.
        /// </summary>
        public required string VersionString { get; init; }
    }

    /// <summary>
    /// Represents a special folder.
    /// </summary>
    public class SpecialFolderDto
    {
        /// <summary>
        /// Gets or sets the ID.
        /// </summary>
        public required string ID { get; init; }

        /// <summary>
        /// Gets or sets the path.
        /// </summary>
        public required string Path { get; init; }
    }

    /// <summary>
    /// Represents a locale.
    /// </summary>
    public class LocaleDto
    {
        /// <summary>
        /// Gets or sets the code.
        /// </summary>
        public required string Code { get; init; }

        /// <summary>
        /// Gets or sets the English name.
        /// </summary>
        public required string EnglishName { get; init; }

        /// <summary>
        /// Gets or sets the display name.
        /// </summary>
        public required string DisplayName { get; init; }
    }
}

