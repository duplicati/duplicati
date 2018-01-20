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
        protected IChannel<Func<Task>> m_channel;
        protected readonly Task m_worker;
        protected CancellationTokenSource m_workerSource;

        public SingleRunner()
        {
            AutomationExtensions.AutoWireChannels(this, null);
            m_channel = ChannelManager.CreateChannel<Func<Task>>();
            m_workerSource = new System.Threading.CancellationTokenSource();
            m_worker = AutomationExtensions.RunProtected(this, Start);
        }

        private async Task Start()
        {
            try
            {
                var ct = m_workerSource.Token;
                while (!ct.IsCancellationRequested)
                {
                    // Grab next task
                    var nextTask = await m_channel.ReadAsync();

                    // Execute it
                    await nextTask();
                }
            }
            catch(Exception ex)
            {
                this.Dispose();
            }
        }

        protected Task<T> DoRunOnMain<T>(Func<Task<T>> method)
        {
            var res = new TaskCompletionSource<T>();

            Task.Run(async () =>
            {
                try
                {
                    if (m_workerSource.IsCancellationRequested)
                    {
                        res.TrySetCanceled();
                        return;
                    }

                    await m_channel.WriteAsync(async () =>
                    {
                        if (m_workerSource.IsCancellationRequested)
                        {
                            res.TrySetCanceled();
                            return;
                        }

                        try
                        {
                            var r = await method().ConfigureAwait(false);
                            res.SetResult(r);
                        }
                        catch (Exception ex)
                        {
                            if (ex is System.Threading.ThreadAbortException)
                                res.TrySetCanceled();
                            else
                                res.TrySetException(ex);
                        }
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (ex is System.Threading.ThreadAbortException)
                        res.TrySetCanceled();
                    else
                        res.TrySetException(ex);
                }
            });

            return res.Task;
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

            if (m_channel != null)
                try { m_channel.Retire(); }
                catch { }
                finally { }

            AutomationExtensions.RetireAllChannels(this);
        }
    }
}

