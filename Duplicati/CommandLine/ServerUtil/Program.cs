using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Duplicati.CommandLine.ServerUtil.Commands;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// The entry point of the application
/// </summary>
public static class Program
{
    /// <summary>
    /// Invokes the builder
    /// </summary>
    /// <param name="args"></param>
    /// <returns>The return code</returns>
    public static Task<int> Main(string[] args)
    {
        Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref args, Library.AutoUpdater.PackageHelper.NamedExecutable.ServerUtil);

        var rootCmd = new RootCommand("Server CLI tool for Duplicati")
            {
                Pause.Create(),
                Resume.Create(),
                ListBackups.Create(),
                RunBackup.Create(),
                Login.Create(),
                ChangePassword.Create(),
                Logout.Create(),
                Import.Create(),
                Export.Create(),
                Health.Create(),
                IssueForeverToken.Create(),
            };

        rootCmd = SettingsBinder.AddGlobalOptions(rootCmd);

        return new CommandLineBuilder(rootCmd)
            .UseDefaults()
            .UseExceptionHandler((ex, context) =>
            {
                if (ex is UserReportedException ure)
                {
                    Console.WriteLine(ure.Message);
                    context.ExitCode = 2;
                }
                else
                {
                    Console.WriteLine(ex.ToString());
                    context.ExitCode = 1;
                }
            })
            .AddMiddleware(async (context, next) =>
            {
                // Inject settings with custom binder
                if (context.ParseResult.CommandResult?.Command is Command cmd)
                    context.BindingContext.AddService(_ => SettingsBinder.GetSettings(context.BindingContext));

                await next(context);
            })
            .Build()
            .InvokeAsync(args);
    }
}
