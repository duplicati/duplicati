using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Duplicati.WebserverCore;

//Useful for debugging ASP.net magically loading controllers
public class ApplicationPartsLogger(ILogger<ApplicationPartsLogger> logger, ApplicationPartManager partManager)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Get the names of all the application parts. This is the short assembly name for AssemblyParts
        var applicationParts = partManager.ApplicationParts.Select(x => x.Name);

        // Create a controller feature, and populate it from the application parts
        var controllerFeature = new ControllerFeature();
        partManager.PopulateFeature(controllerFeature);

        // Get the names of all of the controllers
        var controllers = controllerFeature.Controllers.Select(x => x.Name);

        // Log the application parts and controllers
        logger.LogInformation(
            "Found the following application parts: '{ApplicationParts}' with the following controllers: '{Controllers}'",
            string.Join(", ", applicationParts), string.Join(", ", controllers));

        return Task.CompletedTask;
    }

    // Required by the interface
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}