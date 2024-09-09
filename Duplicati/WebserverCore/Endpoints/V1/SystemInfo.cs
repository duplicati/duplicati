using System.Globalization;
using Duplicati.Library.RestAPI;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class SystemInfo : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/systeminfo", ([FromServices] ILanguageService languageService) => Execute(languageService.GetLanguage())).RequireAuthorization();
        group.MapGet("/systeminfo/filtergroups", () => ExecuteFilterGroups()).RequireAuthorization();
    }

    private static Dto.SystemInfoDto Execute(CultureInfo? browserlanguage)
    {
        browserlanguage ??= CultureInfo.InvariantCulture;

        return new Dto.SystemInfoDto()
        {
            APIVersion = 1,
            PasswordPlaceholder = FIXMEGlobal.PASSWORD_PLACEHOLDER,
            ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
            ServerVersionName = Duplicati.License.VersionNumbers.Version,
            ServerVersionType = Duplicati.Library.AutoUpdater.UpdaterManager.SelfVersion.ReleaseType,
            StartedBy = FIXMEGlobal.Origin,
            DefaultUpdateChannel = Duplicati.Library.AutoUpdater.AutoUpdateSettings.DefaultUpdateChannel.ToString(),
            DefaultUsageReportLevel = Duplicati.Library.UsageReporter.Reporter.DefaultReportLevel,
            ServerTime = DateTime.Now,
            OSType = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "MacOS" : "Unknown",
            DirectorySeparator = Path.DirectorySeparatorChar,
            PathSeparator = Path.PathSeparator,
            CaseSensitiveFilesystem = Duplicati.Library.Utility.Utility.IsFSCaseSensitive,
            MachineName = Environment.MachineName,
            PackageTypeId = Duplicati.Library.AutoUpdater.UpdaterManager.PackageTypeId,
            UserName = OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent().Name : Environment.UserName,
            NewLine = Environment.NewLine,
            CLRVersion = Environment.Version.ToString(),
            CLROSInfo = new Dto.SystemInfoDto.CLROSInfoDto()
            {
                Platform = Environment.OSVersion.Platform.ToString(),
                ServicePack = Environment.OSVersion.ServicePack,
                Version = Environment.OSVersion.Version.ToString(),
                VersionString = Environment.OSVersion.VersionString
            },
            Options = Server.Serializable.ServerSettings.Options,
            CompressionModules = Server.Serializable.ServerSettings.CompressionModules,
            EncryptionModules = Server.Serializable.ServerSettings.EncryptionModules,
            BackendModules = Server.Serializable.ServerSettings.BackendModules,
            GenericModules = Server.Serializable.ServerSettings.GenericModules,
            WebModules = Server.Serializable.ServerSettings.WebModules,
            ConnectionModules = Server.Serializable.ServerSettings.ConnectionModules,
            ServerModules = Server.Serializable.ServerSettings.ServerModules,
            UsingAlternateUpdateURLs = Duplicati.Library.AutoUpdater.AutoUpdateSettings.UsesAlternateURLs,
            LogLevels = Enum.GetNames(typeof(Duplicati.Library.Logging.LogMessageType)),
            SpecialFolders = SpecialFolders.Nodes.Select(n => new Dto.SystemInfoDto.SpecialFolderDto { ID = n.id, Path = n.resolvedpath }),
            BrowserLocale = new Dto.SystemInfoDto.LocaleDto()
            {
                Code = browserlanguage.Name,
                EnglishName = browserlanguage.EnglishName,
                DisplayName = browserlanguage.NativeName
            },
            SupportedLocales = Library.Localization.LocalizationService.SupportedCultures
                .Select(x => new Dto.SystemInfoDto.LocaleDto
                {
                    Code = x,
                    EnglishName = new CultureInfo(x).EnglishName,
                    DisplayName = new CultureInfo(x).NativeName
                }),
            BrowserLocaleSupported = Library.Localization.LocalizationService.isCultureSupported(browserlanguage)
        };
    }

    private static object ExecuteFilterGroups()
        => new { FilterGroups = Library.Utility.FilterGroups.GetFilterStringMap() };
}

