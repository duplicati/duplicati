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

namespace Duplicati.Server
{
    /// <summary>
    /// This class handles synchronized waiting for events
    /// </summary>
    public class EventPollNotify
    {
        /// <summary>
        /// The lock that grants exclusive access to control structures
        /// </summary>
        private readonly object m_lock = new object();
        /// <summary>
        /// The current eventID
        /// </summary>
        private long m_eventNo = 0;
        /// <summary>
        /// The list of subscribed waiting threads
        /// </summary>
        private readonly Queue<System.Threading.ManualResetEvent> m_waitQueue = new Queue<System.Threading.ManualResetEvent>();

        /// <summary>
        /// An eventhandler for subscribing to event updates without blocking
        /// </summary>
        public event EventHandler NewEvent;

        /// <summary>
        /// Gets the current event ID
        /// </summary>
        public long EventNo { get { return m_eventNo; } }

        /// <summary>
        /// Call to wait for an event that is newer than the current known event
        /// </summary>
        /// <param name="eventId">The last known event id</param>
        /// <param name="milliseconds">The number of milliseconds to block</param>
        /// <returns>The current event id</returns>
        public long Wait(long eventId, int milliseconds)
        {
            System.Threading.ManualResetEvent mre;
            lock (m_lock)
            {
                //If a newer event has already occured, return immediately
                if (eventId != m_eventNo)
                    return m_eventNo;

                //Otherwise register this thread as waiting
                mre = new System.Threading.ManualResetEvent(false);
                m_waitQueue.Enqueue(mre);
            }

            //Wait until we are signalled or the time has elapsed
            mre.WaitOne(milliseconds, false);
            return m_eventNo;
        }

        /// <summary>
        /// Signals that an event has occurred and notifies all waiting threads
        /// </summary>
        public void SignalNewEvent()
        {
            lock (m_lock)
            {
                m_eventNo++;
                while (m_waitQueue.Count > 0)
                    m_waitQueue.Dequeue().Set();
            }

            if (NewEvent != null)
                NewEvent(this, null);
        }
    }
}
