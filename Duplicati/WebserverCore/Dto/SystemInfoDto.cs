// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
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
    /// The default URL to present for remote control registration
    /// </summary>
    public required string RemoteControlRegistrationUrl { get; init; }

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
    /// Gets or sets the server time zone.
    /// </summary>
    public required string ServerTimeZone { get; init; }

    /// <summary>
    /// Gets or sets the OS type.
    /// </summary>
    public required string OSType { get; init; }

    /// <summary>
    /// Gets or sets the OS version.
    /// </summary>
    public required string OSVersion { get; init; }

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
    /// Gets or sets the secret provider modules.
    /// </summary>
    public required IEnumerable<IDynamicModule> SecretProviderModules { get; init; }

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
    /// The timezones available on the system
    /// </summary>
    public required IEnumerable<TimeZoneDto> TimeZones { get; init; }

    /// <summary>
    /// Represents a timezone.
    /// </summary>
    public class TimeZoneDto
    {
        /// <summary>
        /// Gets or sets the ID.
        /// </summary>
        public required string ID { get; init; }

        /// <summary>
        /// Gets or sets the display name.
        /// </summary>
        public required string DisplayName { get; init; }

        /// <summary>
        /// Gets or sets the base UTC offset.
        /// </summary>
        public required string CurrentUTCOffset { get; init; }
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

