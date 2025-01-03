using System.Reflection;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Middlewares;

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

        var group = application.MapGroup("/api/v1")
            .AddEndpointFilter<LanguageFilter>()
            .AddEndpointFilter<HostnameFilter>();

        if (!string.IsNullOrWhiteSpace(PreSharedKeyFilter.PreSharedKey))
            group = group.AddEndpointFilter<PreSharedKeyFilter>();

        foreach (var endpoint in endpoints)
        {
            var methodMap = endpoint.GetMethod(nameof(IEndpointV1.Map), BindingFlags.Static | BindingFlags.Public);
            methodMap!.Invoke(null, [group]);
        }

        return application;
    }
}