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

        public override void Install(System.Collections.IDictionary stateSaver)
        {
            var localuser = Context.Parameters["localuser"];
            if (localuser != null)
            {
                this.Account = System.ServiceProcess.ServiceAccount.User;
                this.Username = localuser;
            }


            base.Install(stateSaver);
        }
    }
}
