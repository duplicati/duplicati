using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class Pause
{
    public static Command Create() =>
        new Command("pause", "Pauses the server")
        {
            new Argument<string?>("duration", description: "The duration to pause the server for", getDefaultValue: () => null) {
                Arity = ArgumentArity.ZeroOrOne
            },
        }
        .WithHandler(CommandHandler.Create<Settings, string?>(async (settings, duration) =>
        {
            if (string.IsNullOrWhiteSpace(duration))
                Console.WriteLine("Pausing the server indefinitely");
            else
                Console.WriteLine($"Pausing the server for {duration}");

            await (await settings.GetConnection()).Pause(duration);
        }));
}
