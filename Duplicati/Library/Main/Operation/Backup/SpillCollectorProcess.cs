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
using CoCoL;
using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;
using System.Collections.Generic;

namespace Duplicati.Library.Main.Operation.Backup
{
    /// <summary>
    /// This process just waits until all block processes are terminated
    /// and collects the non-written volumes.
    /// All remaining volumes are re-packed into one or more filled
    /// volumes and uploaded
    /// </summary>
    internal static class SpillCollectorProcess
    {
        public static Task Run()
        {
            return AutomationExtensions.RunTask(
                new
                {
                    Input = ChannelMarker.ForRead<IBackendOperation>("SpillPickup"),
                    Output = ChannelMarker.ForWrite<IBackendOperation>("BackendRequests"),
                },

                async self => 
                {
                    var lst = new List<UploadRequest>();

                    while(!self.Input.IsRetired)
                        try
                        {
                            lst.Add(await (UploadRequest)self.Input.ReadAsync());
                        }
                        catch (Exception ex)
                        {
                            if (ex.IsRetiredException())
                                break;
                            throw;
                        }


                    while(lst.Count > 1)
                    {
                        // TODO: Merge
                        
                    }

                    if (lst.Count != 0)
                        foreach(var n in lst)
                            await self.Output.WriteAsync(n);

                }
            );
        }
    }
}

