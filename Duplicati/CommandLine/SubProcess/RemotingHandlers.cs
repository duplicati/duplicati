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
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using LeanIPC;

namespace Duplicati.CommandLine.SubProcess
{
    /// <summary>
    /// Static wrapepr class for serving as a client
    /// </summary>
    public static class RemotingHandlers
    {
        /// <summary>
        /// Runs the RPC client
        /// </summary>
        /// <returns>The client controller.</returns>
        /// <param name="shared">The stream used for reading and writing.</param>
        public static RPCPeer RunClient(Stream shared)
        {
            return RunClient(shared, shared);
        }

        /// <summary>
        /// Runs the RPC client
        /// </summary>
        /// <returns>The client controller.</returns>
        /// <param name="reader">The stream used for reading.</param>
        /// <param name="writer">The stream used for writing.</param>
        public static RPCPeer RunClient(Stream reader, Stream writer)
        {
            return
                new RPCPeer(reader, writer)

                // We allow the server to call these classes in our process
                .RegisterLocallyServedType<BackendProxy>()
                .RegisterLocallyServedType<LogProxy>()

                // We want to use the StreamProxyMapper with an interface
                .RegisterLocalProxyForRemote<StreamProxyMapper, IStreamProxy>()
                .RegisterLocalProxyForRemote<ServerLogDestinationProxy, ISimpleDestination>()

                // And these are simple, so we just send their field contents
                .RegisterPropertyDecomposer<ICommandLineArgument>()
                .RegisterPropertyDecomposer<IFileEntry>()

                // The log entries are simple structs
                .RegisterPropertyDecomposer<Library.Logging.LogEntry>()

                // Connect as a client
                .Start(true);
        }

        /// <summary>
        /// Runs the RPC server
        /// </summary>
        /// <returns>The server controller.</returns>
        /// <param name="shared">The stream used for reading and writing.</param>
        public static RPCServer RunServer(Stream shared)
        {
            return RunServer(shared, shared);
        }

        /// <summary>
        /// Runs the RPC server
        /// </summary>
        /// <returns>The server controller.</returns>
        /// <param name="reader">The stream used for reading.</param>
        /// <param name="writer">The stream used for writing.</param>
        public static RPCServer RunServer(Stream reader, Stream writer)
        {
            return
                (RPCServer)
                new RPCServer(reader, writer)

                // We allow the server to call our streams and logs
                .RegisterLocallyServedType<ServerLogDestinationProxy>()

                // When we get one of these types, we want a proxy for it
                .RegisterLocalProxyForRemote<BackendProxy, IBackendProxy>()
                .RegisterLocalProxyForRemote<LogProxy, ILogProxy>()

                // The client can call our stream wrapper
                .RegisterLocallyServedType<StreamProxyMapper>()

                // And these are simple, so we just send their field contents
                .RegisterPropertyDecomposer<ICommandLineArgument>()
                .RegisterPropertyDecomposer<IFileEntry>()

                // The log entries are simple structs
                .RegisterPropertyDecomposer<Library.Logging.LogEntry>()

                // Connect as server
                .Start(false);
        }

        /// <summary>
        /// Starts a client in a subprocess and returns the connected server interface
        /// </summary>
        /// <returns>The connected server.</returns>
        public static RPCServer StartServerAndClient()
        {
            RPCServer server;
            if (false)
            {
                var pipeid = "spipe." + Guid.NewGuid().ToString();
                var pipeserver = new System.IO.Pipes.NamedPipeServerStream(pipeid);

                server = RunServer(pipeserver);

                // Run the child process
                server.Child = RPCClient.SpawnClientProcess(pipeid, -1);
            }
            else
            {
                System.Net.Sockets.TcpListener serv = null;
                int port = -1;
                var retries = 20;

                while (serv == null && retries-- > 0)
                {
                    try
                    {
                        port = new Random().Next(5000, 8000);
                        var s = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                        s.Start();
                        serv = s;
                    }
                    catch
                    {
                        //Try another port
                    }
                }

                var child = RPCClient.SpawnClientProcess(null, port);
                server = RunServer(new System.Net.Sockets.NetworkStream(serv.AcceptSocket(), true));
                server.Child = child;
                serv.Stop();
            }

            return server;
        }
    }
}
