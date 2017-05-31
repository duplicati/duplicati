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
using System;using System.Collections.Generic;using Duplicati.Server.Serialization;using System.IO;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class UISettings : IRESTMethodGET, IRESTMethodPOST, IRESTMethodPATCH
    {        public void GET(string key, RequestInfo info)        {            if (string.IsNullOrWhiteSpace(key))            {                info.OutputOK(Program.DataConnection.GetUISettingsSchemes());            }            else            {                info.OutputOK(Program.DataConnection.GetUISettings(key));            }        }

		public void POST(string key, RequestInfo info)
		{
			PATCH(key, info);
		}

		public void PATCH(string key, RequestInfo info)
        {
			if (string.IsNullOrWhiteSpace(key))
			{
				info.ReportClientError("Scheme is missing");
				return;
			}

            IDictionary<string, string> data;
			try
			{
				data = Serializer.Deserialize<Dictionary<string, string>>(new StreamReader(info.Request.Body));
			}
			catch (Exception ex)
			{
				info.ReportClientError(string.Format("Unable to parse settings object: {0}", ex.Message));
				return;
			}

			if (data == null)
			{
				info.ReportClientError(string.Format("Unable to parse settings object"));
				return;
			}

            if (info.Request.Method == "POST")
			    Program.DataConnection.SetUISettings(key, data);
            else
                Program.DataConnection.UpdateUISettings(key, data);
			info.OutputOK();
		}
    }
}

