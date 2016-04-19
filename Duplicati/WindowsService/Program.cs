using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.WindowsService
{
    class Program
    {
        public static void Main(string[] args)
        {
            ServiceBase.Run(new ServiceBase[] { new ServiceControl(args) });
        }
    }
}
