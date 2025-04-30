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
using System.Threading.Tasks;
using CoCoL;
using System.Threading;

namespace Duplicati.Library.Main.Operation.Common
{
    /// <summary>
    /// Provides mutually exclusive access to a resource,
    /// by ensuring all methods are executed sequentially
    /// </summary>
    internal abstract class SingleRunner : IDisposable
    {
        protected readonly AsyncLock m_lock = new AsyncLock();
        protected readonly CancellationTokenSource m_workerSource = new CancellationTokenSource();

        protected async Task<T> DoRunOnMain<T>(Func<Task<T>> method)
        {
            m_workerSource.Token.ThrowIfCancellationRequested();

            using (await m_lock.LockAsync())
            {
                m_workerSource.Token.ThrowIfCancellationRequested();
                return await method().ConfigureAwait(false);
            }
        }

        protected Task RunOnMain(Action method)
        {
            return DoRunOnMain<bool>(() =>
            {
                method();
                return Task.FromResult(true);
            });
        }

        protected Task<T> RunOnMain<T>(Func<T> method)
        {
            return DoRunOnMain(() =>
            {
                return Task.FromResult(method());
            });
        }

        protected Task RunOnMain(Func<Task> method)
        {
            return DoRunOnMain(async () => {
                await method().ConfigureAwait(false);
                return true;
            });
        }

        protected Task<T> RunOnMain<T>(Func<Task<T>> method)
        {
            return DoRunOnMain(method);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            m_workerSource.Cancel();
        }
    }
}

