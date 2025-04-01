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
using System.Text;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Class for wrapping a <see cref="Duplicati.Library.Utility.BlockingQueue{T}"/> as an <see cref="System.Collections.Generic.IEnumerable{T}"/>
    /// </summary>
    public class BlockingQueueAsEnumerable<T> : IEnumerable<T>
    {
        private IEnumerator<T> m_enumerator;

        public BlockingQueueAsEnumerable(BlockingQueue<T> queue)
        {
            m_enumerator = new BlockingQueueEnumerator(queue);
        }

        #region IEnumerable implementation
        public System.Collections.IEnumerator GetEnumerator()
        {
            return ((IEnumerable<T>)this).GetEnumerator();
        }
        #endregion

        #region IEnumerable implementation
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            if (m_enumerator == null)
                throw new Exception("Can only return a single enumerator for a blocking queue");

            IEnumerator<T> tmp = m_enumerator;
            m_enumerator = null;
            return tmp;
        }
        #endregion

        private class BlockingQueueEnumerator : IEnumerator<T>
        {
            private readonly BlockingQueue<T> m_queue;
            private readonly bool m_first = true;
            private T m_current;

            public BlockingQueueEnumerator(BlockingQueue<T> queue)
            {
                m_queue = queue;
            }            

            #region IDisposable implementation
            public void Dispose()
            {
            }
            #endregion

            #region IEnumerator implementation
            public bool MoveNext()
            {
                m_current = m_queue.Dequeue();
                return !(object.Equals(m_current, default(T)) && m_queue.Completed);

            }

            public void Reset()
            {
                if (!m_first)
                    throw new System.NotImplementedException();
            }

            public object Current
            {
                get
                {
                    return m_current;
                }
            }
            #endregion

            #region IEnumerator implementation
            T IEnumerator<T>.Current
            {
                get
                {
                    return m_current;
                }
            }
            #endregion
        }
    }

    /// <summary>
    /// Implementation of a blocking queue that serves as a producer/consumer contact point
    /// </summary>
    /// <typeparam name="T">The type of data in the queue</typeparam>
    public class BlockingQueue<T>
    {
        /// <summary>
        /// The lock that protects the data structures
        /// </summary>
        private readonly object m_lock = new object();
        /// <summary>
        /// The queue storing the elements produced
        /// </summary>
        private readonly Queue<T> m_queue = new Queue<T>();
        /// <summary>
        /// A flag indicating if the queue is now empty forever
        /// </summary>
        private bool m_completed = false;
        /// <summary>
        /// The event that signals that items have been produced (consumers wait for this)
        /// </summary>
        private readonly System.Threading.ManualResetEvent m_itemsProduced = new System.Threading.ManualResetEvent(false);
        /// <summary>
        /// The event that signals that items have been consumed (producers wait for this)
        /// </summary>
        private readonly System.Threading.ManualResetEvent m_itemsConsumed = new System.Threading.ManualResetEvent(false);
        /// <summary>
        /// The maximum capacity of the queue, the producers will be blocked if more elements are put into the queue
        /// </summary>
        private readonly long m_maxCapacity = 100;

        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.Utility.BlockingQueue{T}"/> class with the default capacity
        /// </summary>
        public BlockingQueue()
        {
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="Duplicati.Library.Utility.BlockingQueue{T}"/> is completed
        /// </summary>
        public bool Completed { get { return m_completed; } }

        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.Utility.BlockingQueue{T}"/> class
        /// </summary>
        /// <param name='maxCapacity'>The maximum capacity of the queue, the producers will be blocked if more elements are put into the queue</param>
        public BlockingQueue(long maxCapacity)
            : this()
        {
            if (maxCapacity <= 0)
                throw new ArgumentException("The maximum capacity must be a positive number", nameof(maxCapacity));

            m_maxCapacity = maxCapacity;
        }

        /// <summary>
        /// Signals that the producers are done and no more elements will be put into the queue
        /// </summary>
        public void SetCompleted()
        {
            m_completed = true;
            m_itemsProduced.Set();
            m_itemsConsumed.Set();
        }

        /// <summary>
        /// Enqueues the specified item.
        /// </summary>
        /// <param name='item'>The item to put into the queue</param>
        public bool Enqueue(T item)
        {
            while (true)
            {
                lock(m_lock)
                {
                    if (m_completed)
                        return false;

                    if (m_queue.Count < m_maxCapacity)
                    {
                        m_queue.Enqueue(item);
                        m_itemsProduced.Set();
                        return true;
                    }

                    m_itemsConsumed.Reset();
                }
                m_itemsConsumed.WaitOne();
            }
        }

        /// <summary>
        /// Dequeues an element from the queue
        /// </summary>
        /// <returns>The dequeued element or default(T) if there are no more elements</returns>
        public T Dequeue()
        {
            while(true)
            {
                lock(m_lock)
                {
                    if (m_queue.Count > 0)
                    {
                        m_itemsConsumed.Set();
                        return m_queue.Dequeue();
                    }

                    if (m_completed)
                        return default(T);

                    m_itemsProduced.Reset();
                }

                m_itemsProduced.WaitOne();
            }
        }

    }
}

