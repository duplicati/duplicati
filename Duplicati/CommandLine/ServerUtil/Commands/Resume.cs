using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class Resume
{
    public static Command Create()
        => new Command("resume", "Resumes the server")
        .WithHandler(CommandHandler.Create<Settings>(async (settings) =>
                await (await settings.GetConnection()).Resume())
        );
}
