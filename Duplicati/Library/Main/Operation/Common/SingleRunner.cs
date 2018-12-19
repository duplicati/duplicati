//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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
        protected AsyncLock m_lock = new AsyncLock();
        protected CancellationTokenSource m_workerSource = new CancellationTokenSource();

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

