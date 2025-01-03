using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class Health
{
    public static Command Create() =>
        new Command("health", "Checks the server health endpoint")
        .WithHandler(CommandHandler.Create<Settings>(async (settings) =>
        {
            using var client = new HttpClient(new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = settings.Insecure
                       ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                       : null
            })
            {
                BaseAddress = settings.HostUrl,
                Timeout = TimeSpan.FromSeconds(10)
            };

            try
            {
                var response = await client.GetAsync("health");
                response.EnsureSuccessStatusCode();

                Console.WriteLine("Server is healthy");
                return 0;
            }
            catch (HttpRequestException)
            {
                Console.WriteLine("Server is unhealthy");
                return 1;
            }
        })
    );
}
