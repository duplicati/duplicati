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

#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Duplicati.Library.Utility;

/// <summary>
/// Contains extension methods for <see cref="IEnumerable{T}"/>.
/// </summary>
public static class EnumerableExtensions
{
    /// <summary>
    /// Filters a sequence of values to exclude null elements.
    /// </summary>
    /// <param name="source">The sequence to filter.</param>
    /// <returns>A sequence that contains only the non-null elements from the input sequence.</returns>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
        => source.Where(x => x != null)
                .Select(x => x!);

    /// <summary>
    /// Filters a sequence of values to exclude null elements.
    /// </summary>
    /// <param name="source">The sequence to filter.</param>
    /// <returns>A sequence that contains only the non-null elements from the input sequence.</returns>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : struct
        => source.Where(x => x != null)
                .Select(x => x.Value);

    /// <summary>
    /// Filters a sequence of values to exclude null or whitespace elements.
    /// </summary>
    /// <param name="source">The sequence to filter.</param>
    /// <returns>A sequence that contains only the non-null and non-whitespace elements from the input sequence.</returns>
    public static IEnumerable<string> WhereNotNullOrWhiteSpace(this IEnumerable<string?> source)
        => source.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!);
}
