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
using System.Linq;
using System.Threading.Tasks;
using CoCoL;

namespace Duplicati.Library.UsageReporter
{
    public abstract class ShutdownHelper : IDisposable
    {
        /// <summary>
        /// Uses reflection to find all properties and fields that are of type ContinuationChannel
        /// and calls the Retire method on them
        /// </summary>
        /// <param name="item">The instance to examine.</param>
        public static void RetireAllChannels(object item)
        {
            throw new MissingMethodException();
        }

        /// <summary>
        /// Runs a method and disposes this instance afterwards
        /// </summary>
        /// <returns>The task for completion.</returns>
        /// <param name="method">The callback method that does the actual work.</param>
        /// <param name="catchRetiredExceptions">If set to <c>true</c> any RetiredExceptions are caught and ignored.</param>
        protected async Task RunProtected(Func<Task> method, bool catchRetiredExceptions = true)
        {
            try
            {
                using(this)
                    await method();
            }
            catch(AggregateException ex)
            {
                if (catchRetiredExceptions)
                {
                    var lst = from n in ex.Flatten().InnerExceptions
                             where !(n is RetiredException)
                            select n;

                    if (lst.Count() == 0)
                        return;
                    else if (lst.Count() == 1)
                        throw lst.First();
                    else
                        throw new AggregateException(lst);
                }
                    
                if (ex.Flatten().InnerExceptions.Count == 1)
                    throw ex.Flatten().InnerExceptions.First();
                
                throw;
            }
            catch(RetiredException)
            {
                if (!catchRetiredExceptions)
                    throw;
            }
        }

        /// <summary>
        /// Releases all resource used by the <see cref="Duplicati.Library.UsageReporter.ShutdownHelper"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="Duplicati.Library.UsageReporter.ShutdownHelper"/>. The <see cref="Dispose"/> method leaves the
        /// <see cref="Duplicati.Library.UsageReporter.ShutdownHelper"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="Duplicati.Library.UsageReporter.ShutdownHelper"/> so the garbage collector can reclaim the memory
        /// that the <see cref="Duplicati.Library.UsageReporter.ShutdownHelper"/> was occupying.</remarks>
        public void Dispose()
        {
            RetireAllChannels(this);
        }
    }
}

