// Copyright (C) 2026, The Duplicati Team
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
using System.Diagnostics;
using System.IO.Pipes;
using Duplicati.Library.Main.IPC;
using Duplicati.Library.Utility;
using StreamJsonRpc;

namespace Duplicati.CommandLine;

/// <summary>
/// Entry point for the controller server process
/// </summary>
public static class ControllerProcessEntry
{
    /// <summary>
    /// Environment variable that, when set to a truthy value, launches the debugger
    /// before the process connects to the server. The variable is inherited from the
    /// server process, so setting it there is enough to break into the controller process.
    /// </summary>
    public const string DEBUG_ENV_VAR = "DUPLICATI_CONTROLLER_PROCESS_DEBUG";

    /// <summary>
    /// Returns <c>true</c> if the arguments select controller server mode
    /// </summary>
    /// <param name="args">The command line arguments</param>
    public static bool IsControllerServerMode(string[] args)
        => args.Length > 0 && string.Equals(args[0], ControllerPipeHandshake.COMMAND, StringComparison.Ordinal);

    /// <summary>
    /// The time the running operation gets to unwind after the server is gone,
    /// so the engine can run its own cleanup before the process exits
    /// </summary>
    private static readonly TimeSpan AbortGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs the controller server process and ends the process when done.
    /// Returning from Main does not end the process while a foreground thread is
    /// still running, and the engine starts such threads, so the process is ended
    /// explicitly: without a server there is nobody left to report to.
    /// </summary>
    public static int Run(string[] args)
    {
        var exitCode = RunServer();
        Environment.Exit(exitCode);
        return exitCode;
    }

    /// <summary>
    /// Serves the controller until the server disconnects
    /// </summary>
    /// <returns>The exit code</returns>
    private static int RunServer()
    {
        try
        {
            if (Utility.ParseBool(Environment.GetEnvironmentVariable(DEBUG_ENV_VAR), false))
            {
                Console.Error.WriteLine($"Controller process {Environment.ProcessId} is waiting for a debugger");
                Debugger.Launch();
            }

            // The server hands over the pipe name and the token on stdin; the RPC channel is
            // the pipe, so stdout and stderr are free for ordinary output, which the server
            // forwards to its log
            var (pipeName, token) = ControllerPipeHandshake.ReadHandshake(Console.In);

            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect((int)ControllerPipeHandshake.ConnectTimeout.TotalMilliseconds);
            ControllerPipeHandshake.WriteToken(pipe, token);

            var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = RpcJsonOptions.Options };
            var handler = new NewLineDelimitedMessageHandler(pipe, pipe, formatter);

            using var rpc = new JsonRpc(handler);
            using var trace = RpcTracing.AttachStderrListener(rpc);

            // Create server instance - callbacks will be attached via RPC
            var callbacks = rpc.Attach<IControllerRpcCallbacks>();
            using var server = new ControllerRpcServer(callbacks);

            rpc.AddLocalRpcTarget(server);
            rpc.StartListening();

            // Wait for completion
            try
            {
                rpc.Completion.Await();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Controller process lost the connection to the server: {ex.Message}");
            }

            // Without a server there is nobody to report to, so stop any running
            // operation and give it a bounded time to unwind and clean up
            try
            {
                server.AbortAsync().WaitAsync(AbortGracePeriod).Await();
                server.OperationCompletion.WaitAsync(AbortGracePeriod).Await();
            }
            catch
            {
                // The operation is gone or did not unwind in time; the process exits either way
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Controller process error: {ex}");
            return 1;
        }
    }
}
