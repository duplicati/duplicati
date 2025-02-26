// Copyright (C) 2025, The Duplicati Team
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
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Duplicati.CommandLine.SourceTool;

/// <summary>
/// Extensions for <see cref="Command"/>.
/// </summary>
public static class CommandExtensions
{
    /// <summary>
    /// Adds the missing WithHandler method to <see cref="Command"/>.
    /// </summary>
    /// <param name="command">The command to add the handler to.</param>
    /// <param name="handler">The handler to add.</param>
    /// <returns>The command with the handler added.</returns>
    public static Command WithHandler(this Command command, ICommandHandler handler)
    {
        command.Handler = handler;
        return command;
    }
}
