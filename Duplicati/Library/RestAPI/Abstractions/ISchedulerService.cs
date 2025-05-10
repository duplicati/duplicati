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
using System.Collections.Generic;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.WebserverCore.Abstractions;

public interface ISchedulerService
{
    /// <summary>
    /// Gets the current proposed schedule
    /// </summary>
    IList<Tuple<string, DateTime>> GetProposedSchedule();

    /// <summary>
    /// Terminates the thread. Any items still in queue will be removed
    /// </summary>
    /// <param name="wait">True if the call should block until the thread has exited, false otherwise</param>
    void Terminate(bool wait);

    /// <summary>
    /// A snapshot copy of the current schedule list
    /// </summary>
    List<KeyValuePair<DateTime, ISchedule>> Schedule { get; }

    /// <summary>
    /// Forces the scheduler to re-evaluate the order. 
    /// Call this method if something changes
    /// </summary>
    void Reschedule();
}