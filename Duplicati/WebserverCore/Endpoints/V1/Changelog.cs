using Duplicati.Library.AutoUpdater;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Changelog : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/changelog", ([FromQuery(Name = "from-update")] bool? fromUpdate, [FromServices] Connection connection) => Execute(connection, fromUpdate ?? false)).RequireAuthorization();
    }

    private static Dto.ChangelogDto Execute(Connection connection, bool fromUpdate)
    {
        if (fromUpdate)
        {
            var updateInfo = connection.ApplicationSettings.UpdatedVersion;
            if (updateInfo == null)
                throw new NotFoundException("No update found");


            return new Dto.ChangelogDto()
            {
                Version = updateInfo.Version,
                Changelog = updateInfo.ChangeInfo
            };
        }
        else
        {
            var path = Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".", "changelog.txt");
            return new Dto.ChangelogDto()
            {
                Version = UpdaterManager.SelfVersion.Version,
                Changelog = System.IO.File.ReadAllText(path)
            };
        }
    }
}
