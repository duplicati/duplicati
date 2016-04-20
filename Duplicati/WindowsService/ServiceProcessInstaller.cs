using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.WindowsService
{
    public class ServiceProcessInstaller : System.ServiceProcess.ServiceProcessInstaller
    {
        public ServiceProcessInstaller()
        {
            this.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
        }
    }
}
