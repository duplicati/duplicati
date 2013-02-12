#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
    //TODO: Remove this delegate if we upgrade to .Net Framework 3.5, as it is built in
    public delegate TResult Func<T, TResult>(T input);

    /// <summary>
    /// This class puts a filter over an IEnumerable, 
    /// giving the illusion that the sequence only contains certain values.
    /// This is semantically the same as the FindAll function, 
    /// but does not create a copy of the source
    /// </summary>
    /// <typeparam name="T">The type of enumerable to filter</typeparam>
    public class PlugableEnumerable<T> : IEnumerable<T>
    {
		/// <summary>
		///The predicate used to filter the list 
		/// </summary>
		private Predicate<T> m_predicate;
		
		/// <summary>
		///The conversion function 
		/// </summary>
		private Func<T, T> m_converter;
		
		/// <summary>
		///The list being wrapped 
		/// </summary>
		private IEnumerable<T> m_wrappedList;
		
        /// <summary>
        /// An optional call back function that determines if an element is visible or not
        /// </summary>
        public Predicate<T> Predicate 
		{ 
			get { return m_predicate; }
			set { m_predicate = value; }
		}

        /// <summary>
        /// An optional callback method that converts the element returned by the Current method of the enumerator
        /// </summary>
        public Func<T, T> Converter 
		{ 
			get { return m_converter; }
			set { m_converter = value; }
		}

        /// <summary>
        /// The inner list being wrapped
        /// </summary>
        public IEnumerable<T> WrappedList 
		{ 
			get { return m_wrappedList; }
			set { m_wrappedList = value; }
		}

        /// <summary>
        /// Constructs a new filtered enumeration
        /// </summary>
        /// <param name="predicate">The filter function</param>
        /// <param name="converter">The converter function</param>
        /// <param name="wrappedlist">The sequence to filter</param>
        public PlugableEnumerable(Predicate<T> predicate, Func<T, T> converter, IEnumerable<T> wrappedlist)
        {
            Predicate = predicate;
            Converter = converter;
            WrappedList = wrappedlist;
        }

        /// <summary>
        /// Constructs a new filtered enumeration
        /// </summary>
        /// <param name="sequence">The sequence of items to filter</param>
        public PlugableEnumerable(IEnumerable<T> sequence)
        {
            WrappedList = sequence;
        }

        /// <summary>
        /// Constructs a new filtered enumeration
        /// </summary>
        /// <param name="predicate">The filter function</param>
        /// <param name="converter">The converter function</param>
        /// <param name="wrappedlist">The sequence to filter</param>
        public PlugableEnumerable(Predicate<T> predicate, Func<T, T> converter)
        {
            Predicate = predicate;
            Converter = converter;
        }

        /// <summary>
        /// Applies the filtering from this instance to another list
        /// </summary>
        /// <param name="sequence">The sequence to wrap</param>
        /// <returns>A filtered sequence</returns>
        public PlugableEnumerable<T> WrapList(IEnumerable<T> sequence)
        {
            return new PlugableEnumerable<T>(Predicate, Converter, sequence);
        }

        #region IEnumerable<T> Members

        /// <summary>
        /// Gets a new enumerator
        /// </summary>
        /// <returns>An enumerator</returns>
        public IEnumerator<T> GetEnumerator()
        {
            return new FilterableIEnumerableEnumerator(Predicate, Converter, WrappedList.GetEnumerator());
        }

        #endregion

        #region IEnumerable Members

        /// <summary>
        /// Gets a new enumerator
        /// </summary>
        /// <returns>An enumerator</returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return new FilterableIEnumerableEnumerator(Predicate, Converter, WrappedList.GetEnumerator());
        }

        #endregion

        /// <summary>
        /// Constructs an enumerable that wraps another enumerator, filtering elements in the process
        /// </summary>
        public class FilterableIEnumerableEnumerator : IEnumerator<T>
        {
            private IEnumerator<T> m_parent;
            private Predicate<T> m_predicate;
            private Func<T, T> m_converter;

            /// <summary>
            /// Constructs a new enumerator wrapping the parent enumerator
            /// </summary>
            /// <param name="predicate">The predicate each item must match</param>
            /// <param name="parent">The parent enumerator</param>
            public FilterableIEnumerableEnumerator(Predicate<T> predicate, Func<T, T> converter, IEnumerator<T> parent)
            {
                m_predicate = predicate;
                m_converter = converter;
                m_parent = parent;
            }

            #region IEnumerator<T> Members

            /// <summary>
            /// Gets the current element
            /// </summary>
            public T Current
            {
                get
                {
                    if (m_converter == null)
                        return m_parent.Current;
                    else
                        return m_converter(m_parent.Current);
                }
            }

            #endregion

            #region IDisposable Members

            public void Dispose()
            {
                m_parent.Dispose();
            }

            #endregion

            #region IEnumerator Members

            /// <summary>
            /// Gets the current element
            /// </summary>
            object System.Collections.IEnumerator.Current
            {
                get { return this.Current; }
            }

            /// <summary>
            /// Advances the enumerator one element, this may be multiple elements, depending on the filter
            /// </summary>
            /// <returns>True if the operation suceeded, false if there are no more elements</returns>
            public bool MoveNext()
            {
                if (m_predicate == null)
                    return m_parent.MoveNext();

                bool more;
                while (more = m_parent.MoveNext())
                    if (m_predicate(m_parent.Current))
                        return true;

                return false;

            }

            /// <summary>
            /// Resest the enumerator to its start position, this is directly mapped to the wrapping enumerator
            /// </summary>
            public void Reset()
            {
                m_parent.Reset();
            }

            #endregion
        }
    }
}
