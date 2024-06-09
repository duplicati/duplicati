using Duplicati.Library.IO;
using Duplicati.Library.RestAPI;
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Duplicati.WebserverCore.Notifications;
using Duplicati.WebserverCore.Services;
using Duplicati.WebserverCore.Services.Settings;
using WebserverCore.Services;

namespace Duplicati.WebserverCore.Extensions;

public static class ServiceCollectionsExtensions
{
    public static IServiceCollection AddDuplicati(this IServiceCollection services, Connection connection)
    {
        //old part
        services
            .AddSingleton<LiveControls>()
            .AddSingleton(Serializer.JsonSettings)
            .AddSingleton<UpdatePollThread>()
            .AddSingleton<EventPollNotify>()
            .AddSingleton<Scheduler>()
            .AddSingleton(connection);


        //transitional part - services that act as a proxy to old part for various reasons (not accessible in assembly i.e.)
        services.AddSingleton<ILiveControls>(c => c.GetRequiredService<LiveControls>());

        //new part
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddTransient<IStatusService, StatusService>();
        services.AddTransient<IUpdateService, UpdateService>();
        services.AddSingleton<INotificationUpdateService, NotificationUpdateService>();
        services.AddSingleton<IWorkerThreadsManager, WorkerThreadsManager>();
        services.AddSingleton<IScheduler, SchedulerService>();
        services.AddSingleton<IWebsocketAccessor, WebsocketAccessor>();
        services.AddTransient<ILanguageService, LanguageService>();
        services.AddSingleton<ICaptchaProvider, CaptchaService>();
        services.AddSingleton<ICommandlineRunService, CommandlineRunService>();

        return services;
    }
}