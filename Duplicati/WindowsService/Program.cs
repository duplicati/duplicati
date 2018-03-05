using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.WindowsService
{
    public class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            return Duplicati.Library.AutoUpdater.UpdaterManager.RunFromMostRecent(typeof(Program).GetMethod("RealMain"), args, Duplicati.Library.AutoUpdater.AutoUpdateStrategy.Never);
        }

        public static void RealMain(string[] args)
        {
            var install = args != null && args.Where(x => string.Equals("install", x, StringComparison.OrdinalIgnoreCase)).Any();
            var uninstall = args != null && args.Where(x => string.Equals("uninstall", x, StringComparison.OrdinalIgnoreCase)).Any();
            var help = args != null && args.Where(x => string.Equals("help", x, StringComparison.OrdinalIgnoreCase)).Any();

            if (help)
            {
                Console.WriteLine("This is a Windows Service wrapper tool that hosts the Duplicati.Server.exe process, letting it run as a windows service.");
                Console.WriteLine("|To run from a console, run the Duplicati.Server.exe instead of Duplicati.WindowsService.exe");
                Console.WriteLine();
                Console.WriteLine("Supported commands:");
                Console.WriteLine("  install:\r\n    Installs the service");
                Console.WriteLine("  uninstall:\r\n    Uninstalls the service");
                Console.WriteLine();
                Console.WriteLine("Supported options for the install command:");
                Console.WriteLine("  /localuser:\r\n    Installs the service as a local user");
                Console.WriteLine();
                Console.WriteLine("It is possible to pass arguments to Duplicati.Server.exe, simply add them to the commandline:");
                Console.WriteLine("  TrustBackup.WindowsService.exe install --webservice-interface=loopback --log-retention=3M");
                Console.WriteLine();
                Console.WriteLine("To see supported options, run Duplicati.Server.exe:");
                Console.WriteLine("  TrustBackup.Server.exe help");
                Console.WriteLine();
                Console.WriteLine("To debug the WindowsService setup, add the --debug-service:");
                Console.WriteLine("  TrustBackup.WindowsService.exe install --debug-service");
            }
            else if (install || uninstall)
            {
                // Remove the install and uninstall flags if they are present
                var commandline = string.Join(" ", args.Where(x => !(string.Equals("install", x, StringComparison.OrdinalIgnoreCase) || string.Equals("uninstall", x, StringComparison.OrdinalIgnoreCase))));
                var selfexec = Assembly.GetExecutingAssembly().Location;


                // --uninstall + --install = reinstall
                if (uninstall)
                    ManagedInstallerClass.InstallHelper(new string[] { "/u", selfexec });
                if (install)
                    ManagedInstallerClass.InstallHelper(new string[] { "/commandline=" + commandline, selfexec });
            }
            else
            {
                ServiceBase.Run(new ServiceBase[] { new ServiceControl(args) });
            }
        }
    }
}
