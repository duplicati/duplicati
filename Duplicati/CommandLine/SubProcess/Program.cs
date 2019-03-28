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
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;

namespace Duplicati.CommandLine.SubProcess
{
    public class Program
    {
        public const string ENV_VAR_PIPE_NAME = "SUBP_RUNNER_PIPE";
        public const string ENV_VAR_PORT_NAME = "SUBP_RUNNER_PORT";

        public static int Main(string[] args)
        {
            var pipeid = Environment.GetEnvironmentVariable(ENV_VAR_PIPE_NAME);
            if (!string.IsNullOrWhiteSpace(pipeid))
            {
                // Forward all log messages to any registered log proxies
                using (Library.Logging.Log.StartScope(LogProxy.WriteMessage))
                using (var pipe = new System.IO.Pipes.NamedPipeClientStream(".", pipeid))
                    RemotingHandlers.RunClient(pipe).MainTask.Wait();
                return 0;
            }

            var port = Environment.GetEnvironmentVariable(ENV_VAR_PORT_NAME);
            if (!string.IsNullOrWhiteSpace(port))
            {
                // Forward all log messages to any registered log proxies
                using (Library.Logging.Log.StartScope(LogProxy.WriteMessage))
                using (var cl = new System.Net.Sockets.TcpClient())
                {
                    cl.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, int.Parse(port)));
                    using (var s = cl.GetStream())
                        RemotingHandlers.RunClient(s).MainTask.Wait();

                    return 0;
                }
            }

#if DEBUG
            // Wire up the logging output before starting the server so we have it in the call stack
            // when the log methods are invoked from the client
            using (Library.Logging.Log.StartScope(m => Console.WriteLine(m.FormattedMessage), _ => true))
            {
                var res = RemotingHandlers.StartServerAndClient();

                using (res.SetupLogging().Result)
                {
                    res.TestLogging("log pingback test");

                    var t = res.CreateInstance("file://" + System.IO.Directory.GetCurrentDirectory(), new Dictionary<string, string>(), true);
                    var r = t.Result;

                    //Console.WriteLine(r.DisplayName);
                    //Console.WriteLine(r.Description);
                    //foreach (var p in r.SupportedCommands)
                    //Console.WriteLine(p.ShortDescription);

                    var filename = "my-test.txt";
                    var testdata = "Hello World";
                    var files = r.ListAsync().Result;
                    if (files.Any(x => x.Name == filename))
                        r.DeleteAsync(filename);

                    using (var s = new System.IO.MemoryStream())
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(testdata);
                        s.Write(bytes, 0, bytes.Length);
                        s.Position = 0;

                        using (var sw = new StreamProxyMapper(s))
                            r.PutAsync(filename, sw).Wait();
                    }

                    using (var s = new System.IO.MemoryStream())
                    {
                        using (var sw = new StreamProxyMapper(s))
                            r.GetAsync(filename, sw).Wait();
                        var str = System.Text.Encoding.UTF8.GetString(s.ToArray());
                        if (str != testdata)
                            throw new Exception("Bad result");
                    }
                }

                res.StopAsync().Wait();
            }
#endif

            Console.WriteLine("This program is not intended to be manually invoked. It is used to run parts of Duplicati in separate processes");
            return 1;
        }
    }
}
