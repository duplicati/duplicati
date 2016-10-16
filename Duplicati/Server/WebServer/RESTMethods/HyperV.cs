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
using System;using System.Collections.Generic;using Duplicati.Library.Interface;
using System.Linq;
using Duplicati.Library.Snapshots;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class HyperV : IRESTMethodGET, IRESTMethodDocumented
    {
        public void GET(string key, RequestInfo info)        {
            var hypervUtility = new HyperVUtility();

            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    hypervUtility.QueryHyperVGuestsInfo();
                    info.OutputOK(hypervUtility.Guests.Select(x => new { id = x.ID, name = x.Name }).ToList());

                }
                else
                {
                    var parts = (key ?? "").Split(new char[] { '/' });
                    var path = Duplicati.Library.Utility.Uri.UrlDecode((parts.Length == 2 ? parts.FirstOrDefault() : key ?? ""));
                    var command = parts.Length == 2 ? parts.Last() : null;
                    
                    hypervUtility.QueryHyperVGuestsInfo(true);
                    info.OutputOK(hypervUtility.Guests.Select(x => new { id = x.ID, name = x.Name }).ToList());

                }
            }
            catch (Exception ex)
            {
                info.ReportClientError("Failed to enumerate Hyper-V virtual machines: " + ex.Message);
            }        }        public string Description { get { return "Return a list of Hyper-V virtual machines"; } }        public IEnumerable<KeyValuePair<string, Type>> Types        {            get            {                return new KeyValuePair<string, Type>[] {                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(ICommandLineArgument[]))                };            }        }    }
}

