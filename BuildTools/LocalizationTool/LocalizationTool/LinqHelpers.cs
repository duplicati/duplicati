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
using System.Linq;
using System.Text;

namespace LocalizationTool
{
    public static class LinqHelpers
    {
        //
        // Summary:
        //     Creates a System.Collections.Generic.Dictionary<TKey,TValue> from an System.Collections.Generic.IEnumerable<T>
        //     according to a specified key selector function.
        //
        // Parameters:
        //   source:
        //     An System.Collections.Generic.IEnumerable<T> to create a System.Collections.Generic.Dictionary<TKey,TValue>
        //     from.
        //
        //   keySelector:
        //     A function to extract a key from each element.
        //
        // Type parameters:
        //   TSource:
        //     The type of the elements of source.
        //
        //   TKey:
        //     The type of the key returned by keySelector.
        //
        // Returns:
        //     A System.Collections.Generic.Dictionary<TKey,TValue> that contains keys and
        //     values.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     source or keySelector is null.  -or- keySelector produces a key that is null.
        public static Dictionary<TKey, TSource> ToSafeDictionary<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, string locationName)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (keySelector == null)
                throw new ArgumentNullException("keySelector");

            Dictionary<TKey, TSource> res = new Dictionary<TKey, TSource>();
            foreach (TSource el in source)
            {
                TKey k = keySelector(el);
                if (res.ContainsKey(k))
                    Console.WriteLine("*** Warning, duplicate key: " + k.ToString() + " in " + locationName);
                else
                    res.Add(k, el);
            }

            return res;
        }
        //
        // Summary:
        //     Creates a System.Collections.Generic.Dictionary<TKey,TValue> from an System.Collections.Generic.IEnumerable<T>
        //     according to specified key selector and element selector functions.
        //
        // Parameters:
        //   source:
        //     An System.Collections.Generic.IEnumerable<T> to create a System.Collections.Generic.Dictionary<TKey,TValue>
        //     from.
        //
        //   keySelector:
        //     A function to extract a key from each element.
        //
        //   elementSelector:
        //     A transform function to produce a result element value from each element.
        //
        // Type parameters:
        //   TSource:
        //     The type of the elements of source.
        //
        //   TKey:
        //     The type of the key returned by keySelector.
        //
        //   TElement:
        //     The type of the value returned by elementSelector.
        //
        // Returns:
        //     A System.Collections.Generic.Dictionary<TKey,TValue> that contains values
        //     of type TElement selected from the input sequence.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     source or keySelector or elementSelector is null.  -or- keySelector produces
        //     a key that is null.
        public static Dictionary<TKey, TElement> ToDictionary<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, string locationName)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (keySelector == null)
                throw new ArgumentNullException("keySelector");
            if (elementSelector == null)
                throw new ArgumentNullException("elementSelector");

            Dictionary<TKey, TElement> res = new Dictionary<TKey, TElement>();
            foreach (TSource el in source)
            {
                TKey k = keySelector(el);
                if (res.ContainsKey(k))
                    Console.WriteLine("*** Warning, duplicate key: " + k.ToString() + " in " + locationName);
                else
                    res.Add(k, elementSelector(el));
            }

            return res;
        }
        //
        // Summary:
        //     Creates a System.Collections.Generic.Dictionary<TKey,TValue> from an System.Collections.Generic.IEnumerable<T>
        //     according to a specified key selector function and key comparer.
        //
        // Parameters:
        //   source:
        //     An System.Collections.Generic.IEnumerable<T> to create a System.Collections.Generic.Dictionary<TKey,TValue>
        //     from.
        //
        //   keySelector:
        //     A function to extract a key from each element.
        //
        //   comparer:
        //     An System.Collections.Generic.IEqualityComparer<T> to compare keys.
        //
        // Type parameters:
        //   TSource:
        //     The type of the elements of source.
        //
        //   TKey:
        //     The type of the keys returned by keySelector.
        //
        // Returns:
        //     A System.Collections.Generic.Dictionary<TKey,TValue> that contains keys and
        //     values.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     source or keySelector is null.  -or- keySelector produces a key that is null.
        public static Dictionary<TKey, TSource> ToDictionary<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer, string locationName)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (keySelector == null)
                throw new ArgumentNullException("keySelector");

            Dictionary<TKey, TSource> res = new Dictionary<TKey, TSource>(comparer);
            foreach (TSource el in source)
            {
                TKey k = keySelector(el);
                if (res.ContainsKey(k))
                    Console.WriteLine("*** Warning, duplicate key: " + k.ToString() + " in " + locationName);
                else
                    res.Add(k, el);
            }

            return res;
        }
        //
        // Summary:
        //     Creates a System.Collections.Generic.Dictionary<TKey,TValue> from an System.Collections.Generic.IEnumerable<T>
        //     according to a specified key selector function, a comparer, and an element
        //     selector function.
        //
        // Parameters:
        //   source:
        //     An System.Collections.Generic.IEnumerable<T> to create a System.Collections.Generic.Dictionary<TKey,TValue>
        //     from.
        //
        //   keySelector:
        //     A function to extract a key from each element.
        //
        //   elementSelector:
        //     A transform function to produce a result element value from each element.
        //
        //   comparer:
        //     An System.Collections.Generic.IEqualityComparer<T> to compare keys.
        //
        // Type parameters:
        //   TSource:
        //     The type of the elements of source.
        //
        //   TKey:
        //     The type of the key returned by keySelector.
        //
        //   TElement:
        //     The type of the value returned by elementSelector.
        //
        // Returns:
        //     A System.Collections.Generic.Dictionary<TKey,TValue> that contains values
        //     of type TElement selected from the input sequence.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     source or keySelector or elementSelector is null.  -or- keySelector produces
        //     a key that is null.
        public static Dictionary<TKey, TElement> ToDictionary<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer, string locationName)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (keySelector == null)
                throw new ArgumentNullException("keySelector");
            if (elementSelector == null)
                throw new ArgumentNullException("elementSelector");

            Dictionary<TKey, TElement> res = new Dictionary<TKey, TElement>(comparer);
            foreach (TSource el in source)
            {
                TKey k = keySelector(el);
                if (res.ContainsKey(k))
                    Console.WriteLine("*** Warning, duplicate key: " + k.ToString() + " in " + locationName);
                else
                    res.Add(k, elementSelector(el));
            }

            return res;
        }
    }
}
