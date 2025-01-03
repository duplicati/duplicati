using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.RestAPI;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public record WebModules : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/webmodules", ExecuteGet).RequireAuthorization();
        group.MapPost("/webmodule/{modulekey}", ([FromRoute] string modulekey, [FromBody] Dictionary<string, string> options, CancellationToken cancellationToken) => ExecutePost(modulekey, options, cancellationToken)).RequireAuthorization();
    }

    private static IEnumerable<IWebModule> ExecuteGet()
        => Library.DynamicLoader.WebLoader.Modules;

    private static async Task<Dto.WebModuleOutputDto> ExecutePost(string modulekey, Dictionary<string, string> inputOptions, CancellationToken cancellationToken)
    {
        var m = Library.DynamicLoader.WebLoader.Modules.FirstOrDefault(x => x.Key.Equals(modulekey, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotFoundException("No such module found");

        var options = Runner.GetCommonOptions();
        foreach (var k in inputOptions.Keys)
            options[k] = inputOptions[k];

        await SecretProviderHelper.ApplySecretProviderAsync([], [], options, Library.Utility.TempFolder.SystemTempPath, FIXMEGlobal.SecretProvider, cancellationToken);

        return new Dto.WebModuleOutputDto(
            Status: "OK",
            Result: m.Execute(options)
        );
    }

}
