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
    /// The command that selects controller server mode; must be the first argument
    /// </summary>
    public const string COMMAND = "controller-server";

    /// <summary>
    /// Environment variable that, when set to a truthy value, launches the debugger
    /// before any RPC traffic is handled. The variable is inherited from the server
    /// process, so setting it there is enough to break into the controller process.
    /// </summary>
    public const string DEBUG_ENV_VAR = "DUPLICATI_CONTROLLER_PROCESS_DEBUG";

    /// <summary>
    /// Returns <c>true</c> if the arguments select controller server mode
    /// </summary>
    /// <param name="args">The command line arguments</param>
    public static bool IsControllerServerMode(string[] args)
        => args.Length > 0 && string.Equals(args[0], COMMAND, StringComparison.Ordinal);

    /// <summary>
    /// Runs the controller server process
    /// </summary>
    public static int Run(string[] args)
    {
        try
        {
            if (Utility.ParseBool(Environment.GetEnvironmentVariable(DEBUG_ENV_VAR), false))
            {
                Console.Error.WriteLine($"Controller process {Environment.ProcessId} is waiting for a debugger");
                Debugger.Launch();
            }

            // Setup RPC over stdio. Stdout is reserved for the RPC stream; all other
            // console output is redirected to stderr by Program.Main before this point.
            var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = RpcJsonOptions.Options };
            var handler = new NewLineDelimitedMessageHandler(
                Console.OpenStandardOutput(),
                Console.OpenStandardInput(),
                formatter);

            using var rpc = new JsonRpc(handler);
            using var trace = RpcTracing.AttachStderrListener(rpc);

            // Create server instance - callbacks will be attached via RPC
            var callbacks = rpc.Attach<IControllerRpcCallbacks>();
            using var server = new ControllerRpcServer(callbacks);

            rpc.AddLocalRpcTarget(server);
            rpc.StartListening();

            // Wait for completion
            rpc.Completion.Await();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Controller process error: {ex}");
            return 1;
        }
    }
}
