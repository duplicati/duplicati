namespace Duplicati.WebserverCore
{
    public class DuplicatiWebserver
    {
        public Action Foo() {
            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            app.UseTestMiddleware();
            app.RunAsync("http://localhost:3001");
            return () => { app.StopAsync(); };
        }
    }
}