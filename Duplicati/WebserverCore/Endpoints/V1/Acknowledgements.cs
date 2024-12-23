using Duplicati.Library.Common.IO;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Acknowledgements : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/acknowledgements", () => Execute()).RequireAuthorization();
    }

    private static Dto.AcknowlegdementDto Execute()
    {
        var path = SystemIO.IO_OS.PathCombine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "acknowledgements.txt");
        return new Dto.AcknowlegdementDto()
        {
            Acknowledgements = File.ReadAllText(path)
        };
    }
}
