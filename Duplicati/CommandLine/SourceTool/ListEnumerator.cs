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
using Duplicati.Library.Interface;

namespace Duplicati.CommandLine.SourceTool;

/// <summary>
/// Common methods for the source tool
/// </summary>
public static partial class Common
{
    /// <summary>
    /// Visits all entries in the source provider
    /// </summary>
    /// <param name="source">The source provider to visit</param>
    /// <param name="maxdepth">The maximum depth to visit</param>
    /// <param name="visitor">The visitor function</param>
    /// <param name="token">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public static async Task Visit(ISourceProvider source, int maxdepth, Func<ISourceProviderEntry, int, Task<bool>> visitor, CancellationToken token)
    {
        var visit = new Stack<(ISourceProviderEntry Entry, int Level)>();
        await foreach (var item in source.Enumerate(token))
            visit.Push((item, 0));

        while (visit.Count() != 0)
        {
            var item = visit.Pop();
            var process = await visitor(item.Entry, item.Level);
            if (item.Entry.IsFolder && process && (item.Level < maxdepth || maxdepth <= 0))
            {
                await foreach (var subitem in item.Entry.Enumerate(token))
                {
                    if (subitem.IsFolder)
                        visit.Push((subitem, item.Level + 1));
                    else
                        await visitor(subitem, item.Level);
                }
            }
        }
    }
}
