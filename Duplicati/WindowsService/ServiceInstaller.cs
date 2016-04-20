using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.WindowsService
{
    //[System.ComponentModel.RunInstaller(true)]
    public class ServiceInstaller : System.ServiceProcess.ServiceInstaller
    {
        public ServiceInstaller()
        {
            this.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
            this.ServiceName = ServiceControl.SERVICE_NAME;
            this.DisplayName = ServiceControl.DISPLAY_NAME;
            this.Description = ServiceControl.DESCRIPTION;
        }
    }
}
