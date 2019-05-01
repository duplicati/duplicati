using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
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
            var install = args != null && args.Any(x => string.Equals("install", x, StringComparison.OrdinalIgnoreCase));
            var uninstall = args != null && args.Any(x => string.Equals("uninstall", x, StringComparison.OrdinalIgnoreCase));
            var help = args != null && args.Any(x => string.Equals("help", x, StringComparison.OrdinalIgnoreCase));

            if (help)
            {
                Console.WriteLine("This is a Windows Service wrapper tool that hosts the Duplicati.Server.exe process, letting it run as a windows service.");
                Console.WriteLine("|To run from a console, run the Duplicati.Server.exe instead of Duplicati.WindowsService.exe");
                Console.WriteLine();
                Console.WriteLine("Supported commands (Must be run as Administrator):");
                Console.WriteLine("  install:\r\n    Installs the service");
                Console.WriteLine("  uninstall:\r\n    Uninstalls the service");
                Console.WriteLine();
                Console.WriteLine("It is possible to pass arguments to Duplicati.Server.exe, simply add them to the commandline:");
                Console.WriteLine("  Duplicati.WindowsService.exe install --webservice-interface=loopback --log-retention=3M");
                Console.WriteLine();
                Console.WriteLine("To see supported options, run Duplicati.Server.exe:");
                Console.WriteLine("  Duplicati.Server.exe help");
                Console.WriteLine();
                Console.WriteLine("To debug the WindowsService setup, add the --debug-service:");
                Console.WriteLine("  Duplicati.WindowsService.exe install --debug-service");
            }
            else if (install || uninstall)
            {
                // Remove the install and uninstall flags if they are present
                var commandline = Library.Utility.Utility.WrapAsCommandLine(args.Where(x => !(string.Equals("install", x, StringComparison.OrdinalIgnoreCase) || string.Equals("uninstall", x, StringComparison.OrdinalIgnoreCase))));
                var selfexec = Assembly.GetExecutingAssembly().Location;
                
                // --uninstall + --install = reinstall
                if (uninstall)
                {
                    try
                    {
                        ServiceInstaller.DeleteService(ServiceControl.SERVICE_NAME);
                        Console.WriteLine("Duplicati service delete succeeded.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Duplicati service delete failed. Exception: {0}", ex.Message);
                    }
                }
                if (install)
                {
                    //The fully qualified path to the service binary file. If the path contains a space, it must be quoted so that it is correctly interpreted. For example, "d:\my share\myservice.exe" should be specified as ""d:\my share\myservice.exe"".
                    //The path can also include arguments for an auto-start service. For example, "d:\myshare\myservice.exe arg1 arg2". These arguments are passed to the service entry point (typically the main function).
                    try
                    {
                        ServiceInstaller.InstallService(ServiceControl.SERVICE_NAME, ServiceControl.DISPLAY_NAME, "\"" + selfexec + "\"" + " " + commandline);
                        Console.WriteLine("Duplicati service installation succeeded.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Duplicati service installation failed. Exception: {0}", ex.Message);
                    }
                }
            }
            else
            {
                ServiceBase.Run(new ServiceBase[] { new ServiceControl(args) });
            }
        }
    }
}
