using Duplicati.Library.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public record WebModules : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/webmodules", ExecuteGet).RequireAuthorization();
        group.MapPost("/webmodule/{modulekey}", ([FromRoute] string modulekey, [FromBody] Dictionary<string, string> options) => ExecutePost(modulekey, options)).RequireAuthorization();
    }

    private static IEnumerable<IWebModule> ExecuteGet()
        => Library.DynamicLoader.WebLoader.Modules;

    private static Dto.WebModuleOutputDto ExecutePost(string modulekey, Dictionary<string, string> options)
    {
        var m = Library.DynamicLoader.WebLoader.Modules.FirstOrDefault(x => x.Key.Equals(modulekey, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotFoundException("No such module found");

        return new Dto.WebModuleOutputDto(
            Status: "OK",
            Result: m.Execute(options)
        );
    }

}
