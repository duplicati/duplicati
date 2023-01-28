using Duplicati.WebserverCorer;

namespace Duplicati.WebserverCore
{
    public class DuplicatiWebserver
    {
        public Action Foo() {
            var builder = WebApplication.CreateBuilder();
            builder.Host.UseRESTHandlers();
            var app = builder.Build();

            app.UseTestMiddleware();
            app.UseRESTHandlerEndpoints();

            app.RunAsync("http://localhost:3001");
            return () => { app.StopAsync(); };
        }

        public Action Bar()
        {
            var builder = Host.CreateDefaultBuilder();
            builder.UseRESTHandlers();
            var app = builder.Build();

            app.RunAsync();
            return () => { app.StopAsync() };
        }
    }
}