// Copyright (C) 2026, The Duplicati Team
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
using Microsoft.Win32;

namespace Duplicati.WindowsService
{
    public class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            PreloadSettingsLoader.ConfigurePreloadSettings(ref args, PackageHelper.NamedExecutable.WindowsService);

            if (!OperatingSystem.IsWindows())
                throw new NotSupportedException("Unsupported Operating System");
            var install = args != null && args.Any(x => string.Equals("install", x, StringComparison.OrdinalIgnoreCase) || string.Equals("install-only", x, StringComparison.OrdinalIgnoreCase));
            var uninstall = args != null && args.Any(x => string.Equals("uninstall", x, StringComparison.OrdinalIgnoreCase));
            var install_agent = args != null && args.Any(x => string.Equals("install-agent", x, StringComparison.OrdinalIgnoreCase) || string.Equals("install-only-agent", x, StringComparison.OrdinalIgnoreCase));
            var uninstall_agent = args != null && args.Any(x => string.Equals("uninstall-agent", x, StringComparison.OrdinalIgnoreCase));
            var reset_password = args != null && args.Any(x => string.Equals("reset-password", x, StringComparison.OrdinalIgnoreCase));
            var install_certs = args != null && args.Any(x => string.Equals("install-certs", x, StringComparison.OrdinalIgnoreCase));
            var remove_certs = args != null && args.Any(x => string.Equals("remove-certs", x, StringComparison.OrdinalIgnoreCase));
            var set_init_password = args != null && args.Any(x => string.Equals("set-init-password", x, StringComparison.OrdinalIgnoreCase));
            var lock_key = args != null && args.Any(x => string.Equals("lock-service-key", x, StringComparison.OrdinalIgnoreCase));
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
                Console.WriteLine("  reset-password:\r\n    Sets a new API password and restarts the service");
                Console.WriteLine("  install-certs:\r\n    Generates and installs TLS certificates, then restarts the service");
                Console.WriteLine("  remove-certs:\r\n    Removes TLS certificates, then restarts the service");
                Console.WriteLine("  set-init-password:\r\n    Sets the initial API password (if not already set), then restarts the service");
                Console.WriteLine("  lock-service-key:\r\n    Recreates the hardened Duplicati service registry key");
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
            else if (set_init_password)
            {
                // Used by the MSI installer to atomically:
                //   1. Wipe the entire Service subkey 
                //   2. Recreate the Service key with the locked-down DACL
                //   3. Write InitPassword into the just-created key
                //      handle in the same process, before any other
                //      thread/process can see the key exist.

                var password = Environment.GetEnvironmentVariable("DUPLICATI__INIT_PASSWORD");
                Environment.SetEnvironmentVariable("DUPLICATI__INIT_PASSWORD", null);

                if (string.IsNullOrWhiteSpace(password))
                {
                    Console.WriteLine("Refusing to set init password: DUPLICATI__INIT_PASSWORD environment variable is not set.");
                    return 1;
                }

                try
                {
                    using (var key = ServiceRegistryKey.RecreateLockedDown())
                        key.SetValue(ServiceControl.INIT_REGISTRY_VALUE, password, RegistryValueKind.String);

                    Console.WriteLine("Init password written to locked-down service registry key.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to write init password: {0}", ex.Message);
                    return 1;
                }
            }
            else if (lock_key)
            {
                try
                {
                    ServiceRegistryKey.RecreateLockedDown();
                    Console.WriteLine("Service registry key recreated locked down.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to lock down service registry key: {0}", ex.Message);
                    return 1;
                }
            }
            else if (reset_password)
            {
                var password = Utility.ReadSecretFromConsole("Enter new password (leave empty to auto-generate): ");

                if (string.IsNullOrEmpty(password))
                {
                    password = GenerateRandomPassword(32);
                    Console.WriteLine("Generated password: {0}", password);
                }
                else
                {
                    var confirm = Utility.ReadSecretFromConsole("Confirm password: ");
                    if (password != confirm)
                    {
                        Console.WriteLine("Passwords do not match.");
                        return 1;
                    }
                }

                try
                {
                    // Create a locked-down key before we restart the service
                    using (var key = ServiceRegistryKey.RecreateLockedDown())
                        key.SetValue(ServiceControl.RESET_REGISTRY_VALUE, password, RegistryValueKind.String);

                    Console.WriteLine("Password written to registry.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to write password to registry: {0}", ex.Message);
                    return 1;
                }

                try
                {
                    ServiceInstaller.StopService(ServiceControl.SERVICE_NAME);
                    Console.WriteLine("Service stopped.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to stop service: {0}", ex.Message);
                    return 1;
                }

                try
                {
                    ServiceInstaller.StartService(ServiceControl.SERVICE_NAME);
                    Console.WriteLine("Service started.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to start service: {0}", ex.Message);
                    return 1;
                }

                Console.WriteLine("Password reset complete.");
            }
            else if (install_certs || remove_certs)
            {
                var val = install_certs ? "install" : "uninstall";
                try
                {
                    // Create a locked-down key before we restart the service
                    using (var key = ServiceRegistryKey.RecreateLockedDown())
                        key.SetValue(ServiceControl.TLS_CERTS_REGISTRY_VALUE, val, RegistryValueKind.String);

                    Console.WriteLine($"TLS certs mode '{val}' written to registry.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to write TLS certs mode to registry: {ex.Message}");
                    return 1;
                }

                try
                {
                    ServiceInstaller.StopService(ServiceControl.SERVICE_NAME);
                    Console.WriteLine("Service stopped.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to stop service: {0}", ex.Message);
                    return 1;
                }

                try
                {
                    ServiceInstaller.StartService(ServiceControl.SERVICE_NAME);
                    Console.WriteLine("Service started. Waiting for TLS certs operation to complete...");

                    // Poll registry up to 15 seconds to ensure the service processed the command
                    using (var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    {
                        for (int i = 0; i < 30; i++)
                        {
                            using (var key = view.OpenSubKey(ServiceControl.INIT_REGISTRY_KEY, writable: false))
                            {
                                if (key == null || key.GetValue(ServiceControl.TLS_CERTS_REGISTRY_VALUE) == null)
                                    break;
                            }
                            System.Threading.Thread.Sleep(500);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to start service: {0}", ex.Message);
                    return 1;
                }

                Console.WriteLine("TLS certs operation complete.");
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

        /// <summary>
        /// Generates a cryptographically random alphanumeric password.
        /// </summary>
        private static string GenerateRandomPassword(int length)
            => System.Security.Cryptography.RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", length);
    }
}
