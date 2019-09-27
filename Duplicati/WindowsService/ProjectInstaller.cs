using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace Duplicati.WindowsService
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            this.Installers.AddRange(new Installer[] {
                new ServiceProcessInstaller(),
                new ServiceInstaller()
            });
        }

        public override void Install(IDictionary stateSaver)
        {
            var commandline = Context.Parameters["commandline"];
            if (!string.IsNullOrWhiteSpace(commandline))
            {
                var rawpath = Context.Parameters["assemblypath"];
                var path = new StringBuilder(rawpath);
                if (!rawpath.StartsWith("\"", StringComparison.Ordinal) || !rawpath.EndsWith("\"", StringComparison.Ordinal))
                {
                    path.Insert(0, '"');
                    path.Append('"');
                }

                path.Append(" ");
                path.Append(commandline);

                Context.Parameters["assemblypath"] = path.ToString();
            }

            base.Install(stateSaver);
        }

    }
}
