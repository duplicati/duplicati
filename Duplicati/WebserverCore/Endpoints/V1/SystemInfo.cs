using System.Globalization;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class SystemInfo : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/systeminfo", ([FromServices] ILanguageService languageService, [FromServices] ISystemInfoProvider systemInfoProvider) => Execute(systemInfoProvider, languageService.GetLanguage())).RequireAuthorization();
        group.MapGet("/systeminfo/filtergroups", () => ExecuteFilterGroups()).RequireAuthorization();
    }

    private static Dto.SystemInfoDto Execute(ISystemInfoProvider systemInfoProvider, CultureInfo? browserlanguage)
        => systemInfoProvider.GetSystemInfo(browserlanguage);

    private static Dto.FilterGroupsDto ExecuteFilterGroups()
        => new Dto.FilterGroupsDto(Library.Utility.FilterGroups.GetFilterStringMap());
}

