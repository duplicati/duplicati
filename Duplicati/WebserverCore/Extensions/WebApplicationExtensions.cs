using System.Reflection;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication AddEndpoints(this WebApplication application)
    {
        return AddV1(application);
    }

    private static WebApplication AddV1(WebApplication application)
    {
        var mapperInterfaceType = typeof(IEndpointV1);
        var endpoints =
            typeof(WebApplicationExtensions).Assembly.DefinedTypes
                .Where(t => t.ImplementedInterfaces.Contains(mapperInterfaceType))
                .ToArray();
        if (endpoints.Length == 0)
        {
            return application;
        }

        foreach (var endpoint in endpoints)
        {
            var group = application.MapGroup("/api/v1")
                //TODO: make this work with clean environment - can we enable this by disable globally until user sets up some password?  
                //.RequireAuthorization()
                ;
            var methodMap = endpoint.GetMethod(nameof(IEndpointV1.Map), BindingFlags.Static | BindingFlags.Public);
            methodMap!.Invoke(null, [group]);
        }

        return application;
    }
}