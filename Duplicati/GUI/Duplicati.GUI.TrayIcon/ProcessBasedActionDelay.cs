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
using System;
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Utility;

namespace Duplicati.GUI.TrayIcon;

/// <summary>
/// A class that delays the execution of actions.
/// </summary>
public class ProcessBasedActionDelay : IDisposable
{
    /// <summary>
    /// The channel that sends the delayed actions, buffer avoids deadlocks if multiple events are queued before starting.
    /// </summary>
    private readonly IChannel<Action> m_inboundActionChannel = Channel.Create<Action>(name: "UI Action", buffersize: 500);

    /// <summary>
    /// The channel that sends the start signal.
    /// </summary>
    private readonly IChannel<bool> m_initializedChannel = Channel.Create<bool>(name: "UI Initializer");

    /// <summary>
    /// Reference to the task running
    /// </summary>
    private readonly Task m_task;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessBasedActionDelay"/> class.
    /// </summary>
    public ProcessBasedActionDelay()
    {
        m_task = RunProcessor(m_inboundActionChannel.AsReadOnly(), m_initializedChannel.AsReadOnly());
    }

    /// <summary>
    /// Runs the processor process, which pauses until a ready signal is received.
    /// </summary>
    /// <param name="inboundChannel">The channel with actions to be delayed.</param>
    /// <param name="initializedChannel">The channel that sends the start signal.</param>
    /// <returns>The task running the processor process.</returns>
    private static Task RunProcessor(IReadChannelEnd<Action> inboundChannel, IReadChannelEnd<bool> initializedChannel)
        => AutomationExtensions.RunTask(new
        {
            inboundChannel,
            initializedChannel
        }, async (self) =>
        {
            // Wait for initialization
            await self.initializedChannel.ReadAsync();

            while (true)
            {
                var action = await self.inboundChannel.ReadAsync();
                action();
            }

        });

    /// <summary>
    /// Adds a new task to the processor
    /// </summary>
    /// <param name="action">The action to execute</param>
    public void ExecuteAction(Action action)
    {
        // Note: WriteNoWait() is used to avoid waiting for the action to be read,
        // as this would cause deadlocks if called from within the processor.
        // The buffer size should be sufficient to allow for a reasonable number of actions to be queued.
        var task = m_inboundActionChannel.WriteAsync(action);

        // Observe if the channel is full or retired
        Task.WaitAny(task, Task.Delay(500));
        if (task.IsCompleted)
            task.Await();
    }

    /// <summary>
    /// Signals the start of the processor
    /// </summary>
    public void SignalStart()
        => m_initializedChannel.TryWrite(true);

    /// <summary>
    /// Disposes the object
    /// </summary>
    public void Dispose()
    {
        m_inboundActionChannel.RetireAsync(true).Await();
        m_initializedChannel.RetireAsync(true).Await();
    }
}
