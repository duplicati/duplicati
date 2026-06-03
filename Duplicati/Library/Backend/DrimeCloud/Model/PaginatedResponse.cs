// Copyright (C) 2026, The Duplicati Team
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

namespace Duplicati.Library.Backend.DrimeCloud.Model;

/// <summary>
/// Paginated response from Drime Cloud API
/// </summary>
/// <typeparam name="T">Type of items in the data array</typeparam>
public class PaginatedResponse<T>
{
    /// <summary>
    /// Array of items
    /// </summary>
    public List<T> Data { get; set; } = new();

    /// <summary>
    /// Current page number (1-indexed)
    /// </summary>
    public int Current_Page { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int? Last_Page { get; set; }

    /// <summary>
    /// Items per page
    /// </summary>
    public int Per_Page { get; set; }

    /// <summary>
    /// Total number of items
    /// </summary>
    public int? Total { get; set; }

    /// <summary>
    /// Starting item index
    /// </summary>
    public int? From { get; set; }

    /// <summary>
    /// Ending item index
    /// </summary>
    public int? To { get; set; }

    /// <summary>
    /// Current folder info (null if listing root)
    /// </summary>
    public FileEntry? Folder { get; set; }
}
