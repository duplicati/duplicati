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
using System.Collections.Generic;
using Duplicati.Library.Interface;
using System.Linq;
using Duplicati.Library.Snapshots;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class HyperV : IRESTMethodGET, IRESTMethodDocumented
    {
        public void GET(string key, RequestInfo info)
        {
            // Early exit in case we are non-windows to prevent attempting to load Windows-only components
            if (Library.Utility.Utility.IsClientWindows)
                RealGET(key, info);
            else
                info.OutputOK(new string[0]);
        }

        // Make sure the JIT does not attempt to inline this call and thus load
        // referenced types from System.Management here
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void RealGET(string key, RequestInfo info)
        {
            var hypervUtility = new HyperVUtility();

            if (!hypervUtility.IsHyperVInstalled)
            {
                info.OutputOK(new string[0]);
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    hypervUtility.QueryHyperVGuestsInfo();
                    info.OutputOK(hypervUtility.Guests.Select(x => new { id = x.ID, name = x.Name }).ToList());
                }
                else
                {
                    hypervUtility.QueryHyperVGuestsInfo(true);
                    var foundVMs = hypervUtility.Guests.FindAll(x => x.ID.Equals(new Guid(key)));

                    if (foundVMs.Count == 1)
                        info.OutputOK(foundVMs[0].DataPaths.Select(x => new { text = x, id = x, cls = "folder", iconCls = "x-tree-icon-leaf", check = "false", leaf = "true" }).ToList());
                    else
                        info.ReportClientError(string.Format("Cannot find VM with ID {0}.", key));
                }
            }
            catch (Exception ex)
            {
                info.ReportServerError("Failed to enumerate Hyper-V virtual machines: " + ex.Message);
            }
        }

        public string Description { get { return "Return a list of Hyper-V virtual machines"; } }

        public IEnumerable<KeyValuePair<string, Type>> Types
        {
            get
            {
                return new KeyValuePair<string, Type>[] {
                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(ICommandLineArgument[]))
                };
            }
        }

    }
}

