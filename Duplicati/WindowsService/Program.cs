// Copyright (C) 2024, The Duplicati Team
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

namespace Duplicati.WindowsService
{
    public class Program
    {
        [STAThread]
        public static int Main(string[] args)
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
                var selfexec = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Duplicati.WindowsService.exe");

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
                        return 1;
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
                        return 1;
                    }
                }
            }
            else
            {
                ServiceBase.Run(new ServiceBase[] { new ServiceControl(args) });
            }

            return 0;
        }
    }
}
