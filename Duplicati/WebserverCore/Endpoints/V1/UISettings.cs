using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class UISettings : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/uisettings", ([FromServices] Connection connection) => ExecuteGet(connection)).RequireAuthorization();
        group.MapGet("/uisettings/{scheme}", ([FromServices] Connection connection, [FromRoute] string scheme) => ExecuteSchemeGet(connection, scheme)).RequireAuthorization();
        group.MapPatch("/uisettings/{scheme}", ([FromServices] Connection connection, [FromRoute] string scheme, [FromBody] Dictionary<string, string> settings) => ExecutePatch(connection, scheme, settings)).RequireAuthorization();
    }

    private static IEnumerable<string> ExecuteGet(Connection connection)
        => connection.GetUISettingsSchemes();

    private static IDictionary<string, string> ExecuteSchemeGet(Connection connection, string scheme)
        => connection.GetUISettings(scheme);

    private static void ExecutePatch(Connection connection, string scheme, IDictionary<string, string> settings)
        => connection.UpdateUISettings(scheme, settings);
}