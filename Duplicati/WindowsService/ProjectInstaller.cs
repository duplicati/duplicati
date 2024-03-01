// Copyright (C) 2024, The Duplicati Team
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
