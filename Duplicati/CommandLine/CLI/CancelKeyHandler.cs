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

#nullable enable

using System;
using System.IO;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine;

/// <summary>
/// Turns Ctrl+C into a clean stop of the running operation. Without it, Ctrl+C killed the
/// process in the middle of the operation, which could leave the local database to be
/// repaired or recreated (#2839).
/// The first press stops the operation as the stop button in the web UI does, the second
/// aborts it, and a third one ends the process at once.
/// </summary>
public sealed class CancelKeyHandler
{
    /// <summary>
    /// The writer for the messages to the user
    /// </summary>
    private readonly TextWriter m_output;

    /// <summary>
    /// Guards the fields below, since the key press arrives on its own thread
    /// </summary>
    private readonly object m_lock = new object();

    /// <summary>
    /// The controller of the operation that runs now, or the last one that ran
    /// </summary>
    private Library.Main.Controller? m_controller;

    /// <summary>
    /// The number of times the cancel key was pressed while a controller was set
    /// </summary>
    private int m_presses;

    /// <summary>
    /// Creates a handler that writes its messages to <paramref name="output"/>
    /// </summary>
    /// <param name="output">The writer for the messages to the user</param>
    public CancelKeyHandler(TextWriter output)
    {
        m_output = output;
    }

    /// <summary>
    /// Sets the controller that the cancel key acts on. A command can run more than one
    /// operation, so a controller that comes after a stop request is stopped as well.
    /// </summary>
    /// <param name="controller">The controller that is about to run</param>
    public void Track(Library.Main.Controller controller)
    {
        int presses;
        lock (m_lock)
        {
            m_controller = controller;
            presses = m_presses;
        }

        if (presses >= 2)
            controller.AbortAsync().Await();
        else if (presses == 1)
            controller.StopAsync().Await();
    }

    /// <summary>
    /// Handles <see cref="Console.CancelKeyPress"/>
    /// </summary>
    /// <param name="sender">The sender of the event</param>
    /// <param name="e">The event arguments</param>
    public void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Library.Main.Controller? controller;
        int presses;
        lock (m_lock)
        {
            controller = m_controller;
            // Before an operation is set up there is nothing to stop cleanly
            if (controller == null)
                return;
            presses = ++m_presses;
        }

        // From the third press on, the process ends as it did before
        if (presses > 2)
            return;

        e.Cancel = true;
        if (presses == 1)
        {
            m_output.WriteLine(Strings.Program.CancelKeyStopping);
            controller.StopAsync().Await();
        }
        else
        {
            m_output.WriteLine(Strings.Program.CancelKeyAborting);
            controller.AbortAsync().Await();
        }
    }
}
