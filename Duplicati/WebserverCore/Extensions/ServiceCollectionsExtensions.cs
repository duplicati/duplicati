using Duplicati.Library.AutoUpdater;
using Duplicati.Library.IO;
using Duplicati.Library.RestAPI;
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Abstractions;
using Duplicati.Server;
using Duplicati.Server.Serialization;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Duplicati.WebserverCore.Notifications;
using Duplicati.WebserverCore.Services;
using Duplicati.WebserverCore.Services.Settings;

namespace Duplicati.WebserverCore.Extensions;

public static class ServiceCollectionsExtensions
{
    public static IServiceCollection AddDuplicati(this IServiceCollection services)
    {
        //old part
        services.AddSingleton<LiveControls>();
        services.AddSingleton(Serializer.JsonSettings);
        services.AddSingleton<UpdatePollThread>();
        services.AddSingleton<Scheduler>();
        services.AddSingleton<EventPollNotify>();

        //transitional part - services that act as a proxy to old part for various reasons (not accessible in assembly i.e.)
        services.AddSingleton<IUpdateManagerAccessor, UpdateManagerAccessor>();
        services.AddSingleton<ILiveControls>(c => c.GetRequiredService<LiveControls>());
        services.AddSingleton<IBoolParser, BoolParser>();

        //new part
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddTransient<IStatusService, StatusService>();
        services.AddTransient<IUpdateService, UpdateService>();
        services.AddSingleton<INotificationUpdateService, NotificationUpdateService>();
        services.AddSingleton<IWorkerThreadsManager, WorkerThreadsManager>();
        services.AddSingleton<IScheduler, Scheduler>();
        services.AddSingleton<IWebsocketAccessor, WebsocketAccessor>();

        return services;
    }
}