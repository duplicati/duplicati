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
    /// Runs the controller server process
    /// </summary>
    public static int Run(string[] args)
    {
        try
        {
            // Setup RPC over stdio
            var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = RpcJsonOptions.Options };
            var handler = new NewLineDelimitedMessageHandler(
                Console.OpenStandardOutput(),
                Console.OpenStandardInput(),
                formatter);

            using var rpc = new JsonRpc(handler);

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
