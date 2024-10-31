// Copyright (C) 2024, The Duplicati Team
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

using System.Collections.Generic;

namespace Duplicati.Library.Utility;

/// <summary>
/// Simple implementation of a thread-safe hashset
/// </summary>
/// <typeparam name="T">The type of data to store</typeparam>
public class ConcurrentHashSet<T>
{
    /// <summary>
    /// The lock object used to synchronize access to the set
    /// </summary>
    private readonly object m_lock = new object();
    /// <summary>
    /// The actual set that stores the data
    /// </summary>
    private HashSet<T> m_set = new HashSet<T>();

    /// <summary>
    /// Adds an item to the set
    /// </summary>
    /// <param name="item">The item to add</param>
    /// <returns>True if the item was added, false if it already existed</returns>
    public bool Add(T item)
    {
        lock (m_lock)
            return m_set.Add(item);
    }

    /// <summary>
    /// Removes an item from the set
    /// </summary>
    /// <param name="item">The item to remove</param>
    /// <returns>True if the item was removed, false if it did not exist</returns>
    public bool Remove(T item)
    {
        lock (m_lock)
            return m_set.Remove(item);
    }

    /// <summary>
    /// Checks if the set contains an item
    /// </summary>
    /// <param name="item">The item to check for</param>
    /// <returns>True if the item exists in the set, false otherwise</returns>
    public bool Contains(T item)
    {
        lock (m_lock)
            return m_set.Contains(item);
    }

    /// <summary>
    /// Clears the set
    /// </summary>
    public void Clear()
    {
        lock (m_lock)
            m_set.Clear();
    }
}
