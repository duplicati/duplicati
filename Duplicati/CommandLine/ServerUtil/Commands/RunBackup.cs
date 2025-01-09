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
            new Option<bool>("--wait", "Wait for the backup to finish before returning") {
                IsRequired = false
            },
            new Option<int>("--poll-interval", description: "The interval in seconds to poll for backup status", getDefaultValue: () => 5) {
                IsRequired = false,
            },
            new Option<bool>("--quiet", "Do not print progress messages") {
                IsRequired = false
            }
        }
        .WithHandler(CommandHandler.Create<Settings, string, bool, int, bool>(async (settings, backup, wait, pollinterval, quiet) =>
        {
            if (pollinterval < 1)
                throw new UserReportedException("Poll interval must be at least 1 second");

            var connection = await settings.GetConnection();

            var matchingBackup = (await connection.ListBackups())
                .FirstOrDefault(b => string.Equals(b.Name, backup, StringComparison.OrdinalIgnoreCase) || string.Equals(b.ID, backup));

            if (matchingBackup == null)
                throw new UserReportedException("No backup found with supplied ID or name");

            if (!quiet)
                Console.WriteLine($"Running backup {matchingBackup.Name} (ID: {matchingBackup.ID})");
            await connection.RunBackup(matchingBackup.ID);

            if (wait)
            {
                if (!quiet)
                    Console.WriteLine("Waiting for backup to finish...");
                await connection.WaitForBackup(matchingBackup.ID, TimeSpan.FromSeconds(pollinterval), (msg) =>
                {
                    if (!quiet)
                        Console.WriteLine($"[{DateTime.Now}]: {msg}");
                });

                if (!quiet)
                    Console.WriteLine("Backup finished");
            }
        }));
}
