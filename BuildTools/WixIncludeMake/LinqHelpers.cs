using System;
using System.Collections.Generic;

namespace WixIncludeMake
{
	public static class LinqHelpers
	{
        public static Dictionary<TKey, TValue> ToSafeDictionary<TSource, TKey, TValue>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector, string locationName)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (keySelector == null)
                throw new ArgumentNullException("keySelector");
            if (valueSelector == null)
                throw new ArgumentNullException("valueSelector");

            Dictionary<TKey, TValue> res = new Dictionary<TKey, TValue>();
            foreach (TSource el in source)
            {
                TKey k = keySelector(el);
				TValue v = valueSelector(el);
					
                if (res.ContainsKey(k))
                    Console.WriteLine("*** Warning, duplicate key: " + k.ToString() + " in " + locationName);
                else
                    res.Add(k, v);
            }

            return res;
        }
	}
}

