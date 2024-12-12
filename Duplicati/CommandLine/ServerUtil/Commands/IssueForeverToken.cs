using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class IssueForeverToken
{
    public static Command Create() =>
        new Command("issue-forever-token", "Issues a long-lived access token")
        {
        }
        .WithHandler(CommandHandler.Create<Settings>(async (settings) =>
        {
            var token = await (await settings.GetConnection()).CreateForeverToken();
            Console.WriteLine("Token issued:");
            Console.WriteLine(token);
            Console.WriteLine();
        }));
}
