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
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Utility;

public static class RetryHelper
{
    public static async Task<T> Retry<T>(Func<Task<T>> action, int maxRetries, TimeSpan delay, CancellationToken token)
    {
        var attempt = 0;

        while (true)
        {
            try
            {
                return await action();
            }
            catch
            {
                if (token.IsCancellationRequested)
                    throw;
                if (attempt >= maxRetries)
                    throw;

                attempt++;
            }

            await Task.Delay(delay, token);
        }
    }

    public static async Task Retry(Func<Task> action, int maxRetries, TimeSpan delay, CancellationToken token)
        => await Retry(async () => { await action(); return true; }, maxRetries, delay, token);

}
