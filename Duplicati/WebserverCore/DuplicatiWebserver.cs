using Duplicati.WebserverCore.Database;
using Duplicati.WebserverCore.Extensions;
using Duplicati.WebserverCore.Middlewares;
using Microsoft.EntityFrameworkCore;

namespace Duplicati.WebserverCore;

public class DuplicatiWebserver
{
    public IConfiguration Configuration { get; private set; }

    public WebApplication App { get; private set; }

    public IServiceProvider Provider { get; private set; }

    public void InitWebServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Host.UseRESTHandlers();
        builder.Services.AddControllers()
            // This app gets launched by a different assembly, so we need to tell it to look in this one
            .AddApplicationPart(GetType().Assembly);
        builder.Services.AddHostedService<ApplicationPartsLogger>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        var connectionString = builder.Configuration.GetConnectionString("Sqlite");
        builder.Services.AddDbContext<MainDbContext>(o => { o.UseSqlite(connectionString); });

        builder.Services.AddDuplicati();

        Configuration = builder.Configuration;
        App = builder.Build();
        Provider = App.Services;
    }

    public async Task Start()
    {
        App.UseAuthMiddleware();
        App.UseDefaultStaticFiles(Configuration);

        //TODO: remove
        App.MapControllers();

        App.AddEndpoints()
            .UseNotifications(Configuration);

        await App.RunAsync();
    }

    public async Task Stop()
    {
        await App.StopAsync();
    }
}