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
using System;using System.Linq;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class WebModule : IRESTMethodPOST
    {
        public void POST(string key, RequestInfo info)
        {            var m = Duplicati.Library.DynamicLoader.WebLoader.Modules.Where(x => x.Key.Equals(key, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();            if (m == null)            {                info.ReportClientError(string.Format("No such command {0}", key), System.Net.HttpStatusCode.NotFound);                return;            }            info.OutputOK(new {                 Status = "OK",                 Result = m.Execute(info.Request.Form.Where(x => !x.Name.Equals("command", StringComparison.InvariantCultureIgnoreCase)                ).ToDictionary(x => x.Name, x => x.Value))            });            
        }
    }
}

