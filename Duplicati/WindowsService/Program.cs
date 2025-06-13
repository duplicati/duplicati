// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Utility;

namespace Duplicati.WindowsService
{
    public class Program
    {
        
        [STAThread]
        public static int Main(string[] args)
        {
            Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref args, Library.AutoUpdater.PackageHelper.NamedExecutable.WindowsService);

            if (!OperatingSystem.IsWindows())
                throw new NotSupportedException("Unsupported Operating System");
            var install = args != null && args.Any(x => string.Equals("install", x, StringComparison.OrdinalIgnoreCase) || string.Equals("install-only", x, StringComparison.OrdinalIgnoreCase));
            var uninstall = args != null && args.Any(x => string.Equals("uninstall", x, StringComparison.OrdinalIgnoreCase));
            var install_agent = args != null && args.Any(x => string.Equals("install-agent", x, StringComparison.OrdinalIgnoreCase) || string.Equals("install-only-agent", x, StringComparison.OrdinalIgnoreCase));
            var uninstall_agent = args != null && args.Any(x => string.Equals("uninstall-agent", x, StringComparison.OrdinalIgnoreCase));
            var help = args != null && HelpOptionExtensions.IsArgumentAnyHelpString(args);

            if (help)
            {
                Console.WriteLine("This is a Windows Service wrapper tool that hosts the Duplicati.Server.exe process, letting it run as a windows service.");
                Console.WriteLine("|To run from a console, run the Duplicati.Server.exe instead of Duplicati.WindowsService.exe");
                Console.WriteLine();
                Console.WriteLine("Supported commands (Must be run as Administrator):");
                Console.WriteLine("  install:\r\n    Installs and starts the service");
                Console.WriteLine("  install-only:\r\n    Installs the service");
                Console.WriteLine("  uninstall:\r\n    Uninstalls the service");
                Console.WriteLine("  install-agent:\r\n    Installs and starts the agent service");
                Console.WriteLine("  install-only-agent:\r\n    Installs the agent service");
                Console.WriteLine("  uninstall-agent:\r\n    Uninstalls the agent service");
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
                var commandline = Library.Utility.Utility.WrapAsCommandLine(args.Where(x => !(string.Equals("install", x, StringComparison.OrdinalIgnoreCase) || string.Equals("uninstall", x, StringComparison.OrdinalIgnoreCase) || string.Equals("install-only", x, StringComparison.OrdinalIgnoreCase))).ToArray());
                var selfexec = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Duplicati.WindowsService.exe");

                // --uninstall + --install = reinstall
                if (uninstall)
                {
                    try
                    {
                        ServiceInstaller.StopService(ServiceControl.SERVICE_NAME);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Duplicati service stop failed, a reboot may be required. Exception: {0}", ex.Message);
                    }

                    try
                    {
                        ServiceInstaller.DeleteService(ServiceControl.SERVICE_NAME);
                        Console.WriteLine("Duplicati service delete succeeded.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Duplicati service delete failed. Exception: {0}", ex.Message);
                        return 1;
                    }
                }
                if (install)
                {
                    //The fully qualified path to the service binary file. If the path contains a space, it must be quoted so that it is correctly interpreted. For example, "d:\my share\myservice.exe" should be specified as ""d:\my share\myservice.exe"".
                    //The path can also include arguments for an auto-start service. For example, "d:\myshare\myservice.exe arg1 arg2". These arguments are passed to the service entry point (typically the main function).
                    try
                    {
                        ServiceInstaller.InstallService(ServiceControl.SERVICE_NAME, ServiceControl.DISPLAY_NAME, ServiceControl.SERVICE_DESCRIPTION, "\"" + selfexec + "\"" + " SERVER " + commandline);
                        Console.WriteLine("Duplicati service installation succeeded.");
                        if (!args.Any(x => string.Equals("install-only", x, StringComparison.OrdinalIgnoreCase)))
                        {
                            ServiceInstaller.StartService(ServiceControl.SERVICE_NAME);
                            Console.WriteLine("Duplicati service started.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Duplicati service installation failed. Exception: {0}", ex.Message);
                        return 1;
                    }
                }
            }
            else if (install_agent || uninstall_agent)
            {
                // Remove the install and uninstall flags if they are present
                var commandline = Library.Utility.Utility.WrapAsCommandLine(
                    args.Where(x => !(string.Equals("install-agent", x, StringComparison.OrdinalIgnoreCase) || string.Equals("uninstall-agent", x, StringComparison.OrdinalIgnoreCase) || string.Equals("install-only-agent", x, StringComparison.OrdinalIgnoreCase)))
                    .Prepend("run")
                );
                var selfexec = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Duplicati.WindowsService.exe");

                // --uninstall + --install = reinstall
                if (uninstall_agent)
                {
                    try
                    {
                        ServiceInstaller.StopService(ServiceControl.SERVICE_NAME_AGENT);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Duplicati service stop failed, a reboot may be required. Exception: {0}", ex.Message);
                    }

                    try
                    {
                        ServiceInstaller.DeleteService(ServiceControl.SERVICE_NAME_AGENT);
                        Console.WriteLine("Duplicati service delete succeeded.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Duplicati service delete failed. Exception: {0}", ex.Message);
                        return 1;
                    }
                }
                if (install_agent)
                {
                    //The fully qualified path to the service binary file. If the path contains a space, it must be quoted so that it is correctly interpreted. For example, "d:\my share\myservice.exe" should be specified as ""d:\my share\myservice.exe"".
                    //The path can also include arguments for an auto-start service. For example, "d:\myshare\myservice.exe arg1 arg2". These arguments are passed to the service entry point (typically the main function).
                    try
                    {
                        ServiceInstaller.InstallService(ServiceControl.SERVICE_NAME_AGENT, ServiceControl.DISPLAY_NAME_AGENT, ServiceControl.SERVICE_DESCRIPTION_AGENT, "\"" + selfexec + "\"" + " AGENT " + commandline);
                        Console.WriteLine("Duplicati agent service installation succeeded.");
                        if (!args.Any(x => string.Equals("install-only-agent", x, StringComparison.OrdinalIgnoreCase)))
                        {
                            ServiceInstaller.StartService(ServiceControl.SERVICE_NAME_AGENT);
                            Console.WriteLine("Duplicati agent service started.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Duplicati agent service installation failed. Exception: {0}", ex.Message);
                        return 1;
                    }
                }
            }
            else
            {
                if (args != null && args.Take(1).Any(x => string.Equals("agent", x, StringComparison.OrdinalIgnoreCase)))
                {
                    args = args.Skip(1).ToArray();
                    ServiceBase.Run([new ServiceControl(args, PackageHelper.NamedExecutable.Agent)]);
                }
                else if (args != null && args.Take(1).Any(x => string.Equals("server", x, StringComparison.OrdinalIgnoreCase)))
                {
                    args = args.Skip(1).ToArray();
                    ServiceBase.Run([new ServiceControl(args, PackageHelper.NamedExecutable.Server)]);
                }
                else
                {
                    // Legacy install fallback
                    ServiceBase.Run([new ServiceControl(args, PackageHelper.NamedExecutable.Server)]);
                }
            }

            return 0;
        }
    }
}
