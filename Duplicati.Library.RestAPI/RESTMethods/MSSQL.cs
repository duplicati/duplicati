// Copyright (C) 2024, The Duplicati Team
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
using System.Collections.Generic;
using Duplicati.Library.Interface;
using System.Linq;
using System.Security.Principal;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Common;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class MSSQL : IRESTMethodGET, IRESTMethodDocumented
    {
        public void GET(string key, RequestInfo info)
        {
            // Early exit in case we are non-windows to prevent attempting to load Windows-only components
            if (Platform.IsClientWindows)
                RealGET(key, info);
            else
                info.OutputOK(new string[0]);
        }

        // Make sure the JIT does not attempt to inline this call and thus load
        // referenced types from System.Management here
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void RealGET(string key, RequestInfo info)
        {
            var mssqlUtility = new MSSQLUtility();

            if (!mssqlUtility.IsMSSQLInstalled || !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                info.OutputOK(new string[0]);
                return;
            }

            try
            {
                mssqlUtility.QueryDBsInfo();

                if (string.IsNullOrEmpty(key))
                    info.OutputOK(mssqlUtility.DBs.Select(x => new { id = x.ID, name = x.Name }).ToList());
                else
                {
                    var foundDBs = mssqlUtility.DBs.FindAll(x => x.ID.Equals(key, StringComparison.OrdinalIgnoreCase));

                    if (foundDBs.Count == 1)
                        info.OutputOK(foundDBs[0].DataPaths.Select(x => new { text = x, id = x, cls = "folder", iconCls = "x-tree-icon-leaf", check = "false", leaf = "true" }).ToList());
                    else
                        info.ReportClientError(string.Format("Cannot find DB with ID {0}.", key), System.Net.HttpStatusCode.NotFound);
                }
            }
            catch (Exception ex)
            {
                info.ReportServerError("Failed to enumerate Microsoft SQL Server databases: " + ex.Message);
            }
        }

        public string Description { get { return "Return a list of Microsoft SQL Server databases"; } }

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

