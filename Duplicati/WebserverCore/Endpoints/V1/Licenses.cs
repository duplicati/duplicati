using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Licenses : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/licenses", () => Execute()).RequireAuthorization();
    }

    private static IEnumerable<Dto.LicenseDto> Execute()
    {
        var exefolder = Path.GetDirectoryName(Duplicati.Library.Utility.Utility.getEntryAssembly().Location) ?? ".";
        var path = Path.Combine(exefolder, "licenses");
        if (OperatingSystem.IsMacOS() && !Directory.Exists(path))
        {
            // Go up one, as the licenses cannot be in the binary folder in MacOS Packages
            exefolder = Path.GetDirectoryName(exefolder) ?? ".";
            var test = Path.Combine(exefolder, "Licenses");
            if (Directory.Exists(test))
                path = test;
        }
        return Duplicati.License.LicenseReader.ReadLicenses(path)
            .Select(x => new Dto.LicenseDto
            {
                Title = x.Title,
                Url = x.Url,
                License = x.License,
                Jsondata = x.Jsondata
            });

    }
}
