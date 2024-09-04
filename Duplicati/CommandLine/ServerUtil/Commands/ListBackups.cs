using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class ListBackups
{
    public static Command Create() =>
        new Command("list-backups", "List all backups")
        .WithHandler(CommandHandler.Create<Settings>(async (settings) =>
        {
            var bks = await (await settings.GetConnection()).ListBackups();

            if (!bks.Any())
            {
                Console.WriteLine("No backups found");
                return;
            }

            foreach (var bk in bks)
            {
                Console.WriteLine($"{bk.ID}: {bk.Name}");
                if (!string.IsNullOrEmpty(bk.Description))
                    Console.WriteLine($"  {bk.Description}");
                Console.WriteLine();
            }
        }));
}
