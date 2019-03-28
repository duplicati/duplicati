//  Copyright (C) 2019, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Threading.Tasks;

namespace Duplicati.CommandLine.SubProcess
{
    public static class RPCClient
    {
        public static System.Diagnostics.Process SpawnClientProcess(string pipeid, int port)
        {
            var FAKE_SPAWN = true;

            if (FAKE_SPAWN)
            {
                if (string.IsNullOrWhiteSpace(pipeid))
                    Environment.SetEnvironmentVariable(Program.ENV_VAR_PORT_NAME, port.ToString());
                else
                    Environment.SetEnvironmentVariable(Program.ENV_VAR_PIPE_NAME, pipeid);

                Task.Run(() => Program.Main(null));
                return null;
            }
            else
            {
                var p = new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                if (string.IsNullOrWhiteSpace(pipeid))
                    p.EnvironmentVariables[Program.ENV_VAR_PORT_NAME] = port.ToString();
                else
                    p.EnvironmentVariables[Program.ENV_VAR_PIPE_NAME] = pipeid;

                var res = System.Diagnostics.Process.Start(p);

                res.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
                res.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());

                return res;
            }
        }
    }
}
