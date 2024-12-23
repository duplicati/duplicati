using Duplicati.Library.IO;
using Duplicati.Library.RestAPI;
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Duplicati.WebserverCore.Middlewares;
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
        services
            .AddSingleton<ISettingsService, SettingsService>()
            .AddTransient<IStatusService, StatusService>()
            .AddTransient<IUpdateService, UpdateService>()
            .AddSingleton<INotificationUpdateService, NotificationUpdateService>()
            .AddSingleton<IWorkerThreadsManager, WorkerThreadsManager>()
            .AddSingleton<IScheduler, SchedulerService>()
            .AddSingleton<IWebsocketAccessor, WebsocketAccessor>()
            .AddTransient<ILanguageService, LanguageService>()
            .AddSingleton<ICaptchaProvider, CaptchaService>()
            .AddSingleton<ICommandlineRunService, CommandlineRunService>()
            .AddTransient<IJWTTokenProvider, JWTTokenProvider>()
            .AddTransient<ITokenFamilyStore, TokenFamilyStore>()
            .AddTransient<ILoginProvider, LoginProvider>()
            .AddSingleton<IRemoteController, RemoteControllerService>()
            .AddSingleton<IRemoteControllerRegistration, RemoteControllerRegistrationService>()
            .AddSingleton<ISystemInfoProvider, SystemInfoProvider>();

        return services;
    }
}