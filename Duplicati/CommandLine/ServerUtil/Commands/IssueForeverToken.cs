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
            Console.WriteLine("Token issued with a lifetime of 10 years.");
            Console.WriteLine("Make sure you disable the forever token API on the server, to avoid generating new tokens.");
            Console.WriteLine();
            Console.WriteLine($"If you need to revoke the token, you can reset the JWT signing keys by restarting the server with the command '--{"reset-jwt-config"}=true', or the environment variable '{"DUPLICATI__RESET_JWT_CONFIG=true"}'.");
            Console.WriteLine();
            Console.WriteLine("The issued token is:");
            Console.WriteLine($"Authorization: Bearer {token}");
            Console.WriteLine();
        }));
}
