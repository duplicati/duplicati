using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class ChangePassword
{
    public static Command Create() =>
        new Command("change-password", "Changes the server password")
        {
            new Argument<string>("new-password", "The new password to use") {
                Arity = ArgumentArity.ZeroOrOne
            },
        }
        .WithHandler(CommandHandler.Create<Settings, string>(async (settings, newPassword) =>
        {
            // Ask for previous password first, if needed
            var connection = await settings.GetConnection();

            if (string.IsNullOrWhiteSpace(newPassword))
                newPassword = HelperMethods.ReadPasswordFromConsole("Please provide the new password: ");

            if (string.IsNullOrWhiteSpace(newPassword))
                throw new UserReportedException("No password provided");

            await connection.ChangePassword(newPassword);
        }));
}
