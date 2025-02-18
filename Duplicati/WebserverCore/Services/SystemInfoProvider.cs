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
using System.Globalization;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.RestAPI;
using Duplicati.Server;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Produces system information.
/// </summary>
public class SystemInfoProvider : ISystemInfoProvider
{
    /// <summary>
    /// System information that does not change during runtime.
    /// </summary>
    private sealed record StaticSystemInformation
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
        public required Library.Interface.ICommandLineArgument[] Options { get; init; }

        /// <summary>
        /// Gets or sets the compression modules.
        /// </summary>
        public required IDynamicModule[] CompressionModules { get; init; }

        /// <summary>
        /// Gets or sets the encryption modules.
        /// </summary>
        public required IDynamicModule[] EncryptionModules { get; init; }

        /// <summary>
        /// Gets or sets the backend modules.
        /// </summary>
        public required IDynamicModule[] BackendModules { get; init; }

        /// <summary>
        /// Gets or sets the generic modules.
        /// </summary>
        public required IDynamicModule[] GenericModules { get; init; }

        /// <summary>
        /// Gets or sets the web modules.
        /// </summary>
        public required IDynamicModule[] WebModules { get; init; }

        /// <summary>
        /// Gets or sets the connection modules.
        /// </summary>
        public required IDynamicModule[] ConnectionModules { get; init; }

        /// <summary>
        /// Gets or sets the server modules.
        /// </summary>
        public required object[] ServerModules { get; init; }

        /// <summary>
        /// Gets or sets the secret provider modules.
        /// </summary>
        public required IDynamicModule[] SecretProviderModules { get; init; }

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
        public required SystemInfoDto.SpecialFolderDto[] SpecialFolders { get; init; }

        /// <summary>
        /// Gets or sets the supported locales.
        /// </summary>
        public required SystemInfoDto.LocaleDto[] SupportedLocales { get; init; }

        /// <summary>
        /// The timezones available on the system
        /// </summary>
        public required IEnumerable<SystemInfoDto.TimeZoneDto> TimeZones { get; init; }
    }

    /// <summary>
    /// Cached static system information.
    /// </summary>
    private Lazy<StaticSystemInformation> _systemInfoBase = new(() => new StaticSystemInformation
    {
        APIVersion = 1,
        PasswordPlaceholder = FIXMEGlobal.PASSWORD_PLACEHOLDER,
        ServerVersion = UpdaterManager.SelfVersion.Version,
        ServerVersionName = License.VersionNumbers.VERSION_NAME,
        ServerVersionType = UpdaterManager.SelfVersion.ReleaseType,
        RemoteControlRegistrationUrl = Library.RemoteControl.RegisterForRemote.DefaultRegisterationUrl,
        StartedBy = FIXMEGlobal.Origin,
        DefaultUpdateChannel = AutoUpdateSettings.DefaultUpdateChannel.ToString(),
        DefaultUsageReportLevel = Library.UsageReporter.Reporter.DefaultReportLevel,
        OSType = UpdaterManager.OperatingSystemName,
        OSVersion = Library.UsageReporter.OSInfoHelper.PlatformString,
        DirectorySeparator = Path.DirectorySeparatorChar,
        PathSeparator = Path.PathSeparator,
        CaseSensitiveFilesystem = Library.Utility.Utility.IsFSCaseSensitive,
        MachineName = Environment.MachineName,
        PackageTypeId = UpdaterManager.PackageTypeId,
        UserName = OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().Name : Environment.UserName,
        NewLine = Environment.NewLine,
        CLRVersion = Environment.Version.ToString(),
        Options = Server.Serializable.ServerSettings.Options,
        CompressionModules = Server.Serializable.ServerSettings.CompressionModules,
        EncryptionModules = Server.Serializable.ServerSettings.EncryptionModules,
        BackendModules = Server.Serializable.ServerSettings.BackendModules,
        GenericModules = Server.Serializable.ServerSettings.GenericModules,
        WebModules = Server.Serializable.ServerSettings.WebModules,
        ConnectionModules = Server.Serializable.ServerSettings.ConnectionModules,
        ServerModules = Server.Serializable.ServerSettings.ServerModules,
        SecretProviderModules = Server.Serializable.ServerSettings.SecretProviderModules,
        UsingAlternateUpdateURLs = AutoUpdateSettings.UsesAlternateURLs,
        LogLevels = Enum.GetNames(typeof(Library.Logging.LogMessageType)),
        SpecialFolders = SpecialFolders.Nodes.Select(n => new Dto.SystemInfoDto.SpecialFolderDto { ID = n.id, Path = n.resolvedpath }).ToArray(),
        SupportedLocales = Library.Localization.LocalizationService.SupportedCultures
                .Select(x => new Dto.SystemInfoDto.LocaleDto
                {
                    Code = x,
                    EnglishName = new CultureInfo(x).EnglishName,
                    DisplayName = new CultureInfo(x).NativeName
                }).ToArray(),
        TimeZones = Library.Utility.TimeZoneHelper.GetTimeZones()
                .Select(x => new Dto.SystemInfoDto.TimeZoneDto
                {
                    ID = x.Id,
                    DisplayName = x.DisplayName,
                    CurrentUTCOffset = x.CurrentUtcOffset.ToString()
                }),
    });

    /// <inheritdoc />
    public SystemInfoDto GetSystemInfo(CultureInfo? browserlanguage)
    {
        browserlanguage ??= CultureInfo.InvariantCulture;
        var systeminfo = _systemInfoBase.Value;

        // Return the system information, patch in dynamic values
        return new SystemInfoDto()
        {
            APIVersion = systeminfo.APIVersion,
            PasswordPlaceholder = systeminfo.PasswordPlaceholder,
            ServerVersion = systeminfo.ServerVersion,
            ServerVersionName = systeminfo.ServerVersionName,
            ServerVersionType = systeminfo.ServerVersionType,
            RemoteControlRegistrationUrl = systeminfo.RemoteControlRegistrationUrl,
            StartedBy = systeminfo.StartedBy,
            DefaultUpdateChannel = systeminfo.DefaultUpdateChannel,
            DefaultUsageReportLevel = systeminfo.DefaultUsageReportLevel,
            ServerTime = DateTime.Now,
            ServerTimeZone = TimeZoneInfo.Local.Id,
            OSType = systeminfo.OSType,
            OSVersion = systeminfo.OSVersion,
            DirectorySeparator = systeminfo.DirectorySeparator,
            PathSeparator = systeminfo.PathSeparator,
            CaseSensitiveFilesystem = systeminfo.CaseSensitiveFilesystem,
            MachineName = systeminfo.MachineName,
            PackageTypeId = systeminfo.PackageTypeId,
            UserName = systeminfo.UserName,
            NewLine = systeminfo.NewLine,
            CLRVersion = systeminfo.CLRVersion,
            Options = systeminfo.Options,
            CompressionModules = systeminfo.CompressionModules,
            EncryptionModules = systeminfo.EncryptionModules,
            BackendModules = systeminfo.BackendModules,
            GenericModules = systeminfo.GenericModules,
            WebModules = systeminfo.WebModules,
            ConnectionModules = systeminfo.ConnectionModules,
            ServerModules = systeminfo.ServerModules,
            SecretProviderModules = systeminfo.SecretProviderModules,
            UsingAlternateUpdateURLs = systeminfo.UsingAlternateUpdateURLs,
            LogLevels = systeminfo.LogLevels,
            SpecialFolders = systeminfo.SpecialFolders,
            BrowserLocale = new SystemInfoDto.LocaleDto()
            {
                Code = browserlanguage.Name,
                EnglishName = browserlanguage.EnglishName,
                DisplayName = browserlanguage.NativeName
            },
            SupportedLocales = systeminfo.SupportedLocales,
            BrowserLocaleSupported = Library.Localization.LocalizationService.isCultureSupported(browserlanguage),
            TimeZones = systeminfo.TimeZones,
        };
    }
}
