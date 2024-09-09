using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class Logout
{
    public static Command Create()
        => new Command("logout", "Logs out of the server")
        .WithHandler(CommandHandler.Create<Settings>(async (settings) =>
                await (await settings.GetConnection()).Logout(settings))
        );
}
