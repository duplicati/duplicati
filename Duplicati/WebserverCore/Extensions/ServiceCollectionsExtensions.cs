// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
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