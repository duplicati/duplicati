using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class Login
{
    public static Command Create() =>
        new Command("login", "Logs in to the server")
        .WithHandler(CommandHandler.Create<Settings>(async (settings) =>
            {
                Console.WriteLine("Logging in to the server");
                await Connection.Connect(settings, true);

                Console.WriteLine("Logged in, persistent token saved");
            })
        );
}
