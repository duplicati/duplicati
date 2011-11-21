using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Duplicati.GUI.TrayIcon
{
    static class Program
    {
        public static HttpServerConnection Connection;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            //TODO: Fix options for non-hosted
            using (new HostedInstanceKeeper(args))
            {
                using (Connection = new HttpServerConnection(new Uri("http://localhost:8080/control.cgi"), null))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new WindowsMainForm());
                }
            }
        }
    }
}
