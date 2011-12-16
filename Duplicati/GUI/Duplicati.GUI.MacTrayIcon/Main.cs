using System;
using System.Drawing;

namespace TestMonoMac
{
    class MainClass
    {
        //We just call the correct implementation
        static void Main (string [] args)
        {
            Duplicati.GUI.TrayIcon.Program.Main(args);
        }
        
    }
}	

