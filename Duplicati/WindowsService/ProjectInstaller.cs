using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.Threading.Tasks;

namespace Duplicati.WindowsService
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            this.Installers.AddRange(new Installer[] {
                new ServiceProcessInstaller(),
                new ServiceInstaller()
            });
        }
    }
}
