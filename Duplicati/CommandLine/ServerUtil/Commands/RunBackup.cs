using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class RunBackup
{
    public static Command Create() =>
        new Command("run", "Runs a backup")
        {
            new Argument<string>("backup", "The backup to run, either ID or exact name (case-insensitive)") {
                Arity = ArgumentArity.ExactlyOne
            },
        }
        .WithHandler(CommandHandler.Create<Settings, string>(async (settings, backup) =>
        {
            var connection = await settings.GetConnection();

            var matchingBackup = (await connection.ListBackups())
                .FirstOrDefault(b => string.Equals(b.Name, backup, StringComparison.OrdinalIgnoreCase) || string.Equals(b.ID, backup));

            if (matchingBackup == null)
                throw new UserReportedException("No backup found with supplied ID or name");

            Console.WriteLine($"Running backup {matchingBackup.Name} (ID: {matchingBackup.ID})");
            await connection.RunBackup(matchingBackup.ID);
        }));
}
