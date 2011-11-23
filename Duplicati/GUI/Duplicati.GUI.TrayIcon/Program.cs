using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Duplicati.Library.Interface;

namespace Duplicati.GUI.TrayIcon
{
    static class Program
    {
        public static HttpServerConnection Connection;
        
        private const string TOOLKIT_OPTION = "toolkit";
        private const string TOOLKIT_WINDOWS_FORMS = "winforms";
        private const string TOOLKIT_GTK = "gtk";
        private const string TOOLKIT_COCOA = "cocoa";
        
        private static readonly string DEFAULT_TOOLKIT =
            Duplicati.Library.Utility.Utility.IsClientLinux ?
                TOOLKIT_GTK : TOOLKIT_WINDOWS_FORMS;
        
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] _args)
        {
            List<string> args = new List<string>(_args);
            Dictionary<string, string> options = Duplicati.CommandLine.CommandLineParser.ExtractOptions(args);
            
            string toolkit;
            if (!options.TryGetValue(TOOLKIT_OPTION, out toolkit))
                toolkit = DEFAULT_TOOLKIT;
            else 
            {
                if (TOOLKIT_WINDOWS_FORMS.Equals(toolkit, StringComparison.InvariantCultureIgnoreCase))
                    toolkit = TOOLKIT_WINDOWS_FORMS;
                else if (TOOLKIT_GTK.Equals(toolkit, StringComparison.InvariantCultureIgnoreCase))
                    toolkit = TOOLKIT_GTK;
                else if (TOOLKIT_COCOA.Equals(toolkit, StringComparison.InvariantCultureIgnoreCase))
                    toolkit = TOOLKIT_COCOA;
                else
                    toolkit = DEFAULT_TOOLKIT;
            }
            
            
            //TODO: Fix options for non-hosted
            using (new HostedInstanceKeeper(_args))
            {
                using (Connection = new HttpServerConnection(new Uri("http://localhost:8080/control.cgi"), null))
                {
                    if (toolkit == TOOLKIT_WINDOWS_FORMS)
                        new WinFormsRunner().RunMain();
                    else if (toolkit == TOOLKIT_GTK)
                        new GtkRunner().RunMain();
                    /*else if (toolkit == TOOLKIT_COCOA)
                        new CocoaRunner.RunMain();*/
                }
            }
        }
        
        
        public static Duplicati.Library.Interface.ICommandLineArgument[] SupportedCommands
        {
            get
            {
                return new Duplicati.Library.Interface.ICommandLineArgument[]
                {
                    new Duplicati.Library.Interface.CommandLineArgument(TOOLKIT_OPTION, CommandLineArgument.ArgumentType.Enumeration, "Selects the toolkit to use", "Choose the toolkit used to generate the TrayIcon, note that it will fail if the selected toolkit is not supported on this machine", DEFAULT_TOOLKIT, null, new string[] {TOOLKIT_WINDOWS_FORMS, TOOLKIT_GTK, TOOLKIT_COCOA}),
                };
            }
        }
    }
}
