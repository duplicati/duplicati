#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Class for wrapping a <see cref="Duplicati.Library.Utility.BlockingQueue"/> as an <see cref="System.Collections.Generic.IEnumerable"/>
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
            private BlockingQueue<T> m_queue;
            private bool m_first = true;
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
        private object m_lock = new object();
        /// <summary>
        /// The queue storing the elements produced
        /// </summary>
        private Queue<T> m_queue = new Queue<T>();
        /// <summary>
        /// A flag indicating if the queue is now empty forever
        /// </summary>
        private bool m_completed = false;
        /// <summary>
        /// The event that signals that items have been produced (consumers wait for this)
        /// </summary>
        private System.Threading.ManualResetEvent m_itemsProduced = new System.Threading.ManualResetEvent(false);
        /// <summary>
        /// The event that signals that items have been consumed (producers wait for this)
        /// </summary>
        private System.Threading.ManualResetEvent m_itemsConsumed = new System.Threading.ManualResetEvent(false);
        /// <summary>
        /// The maximum capacity of the queue, the producers will be blocked if more elements are put into the queue
        /// </summary>
        private long m_maxCapacity = 100;

        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.Utility.BlockingQueue"/> class with the default capacity
        /// </summary>
        public BlockingQueue()
        {
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="Duplicati.Library.Utility.BlockingQueue"/> is completed
        /// </summary>
        public bool Completed { get { return m_completed; } }

        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.Utility.BlockingQueue"/> class
        /// </summary>
        /// <param name='maxCapacity'>The maximum capacity of the queue, the producers will be blocked if more elements are put into the queue</param>
        public BlockingQueue(long maxCapacity)
            : this()
        {
            if (maxCapacity <= 0)
                throw new ArgumentException("maxCapacity must be a positive number", "maxCapacity");

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
        /// <returns>The dequed element or default(T) if there are no more elements</returns>
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

