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
using System;using System.Linq;using System.Collections.Generic;using Duplicati.Server.Database;using System.IO;using Duplicati.Server.Serialization;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class ServerSettings : IRESTMethodGET, IRESTMethodPATCH, IRESTMethodDocumented    {        public void GET(string key, RequestInfo info)        {            var adv_props = from n in Program.DataConnection.GetSettings(Database.Connection.APP_SETTINGS_ID)                select new KeyValuePair<string, string>(n.Name, n.Value);            info.OutputOK(adv_props.ToDictionary(x => x.Key, x => x.Value));        }        public void PATCH(string key, RequestInfo info)        {            string str = info.Request.Form["data"].Value;            if (string.IsNullOrWhiteSpace(str))                str = new StreamReader(info.Request.Body, System.Text.Encoding.UTF8).ReadToEnd();            if (string.IsNullOrWhiteSpace(str))            {                info.ReportClientError("Missing data object");                return;            }            Dictionary<string, string> data = null;            try            {                data = Serializer.Deserialize<Dictionary<string, string>>(new StringReader(str));                if (data == null)                {                    info.ReportClientError("Data object had no entry");                    return;                }                Program.DataConnection.ApplicationSettings.UpdateSettings(data, false);                info.OutputOK();            }            catch (Exception ex)            {                if (data == null)                    info.ReportClientError(string.Format("Unable to parse data object: {0}", ex.Message));                else                    info.ReportClientError(string.Format("Unable to save settings: {0}", ex.Message));            }                    }        public string Description { get { return "Return a list of settings for the server"; } }        public IEnumerable<KeyValuePair<string, Type>> Types        {            get            {                return new KeyValuePair<string, Type>[] {                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(ApplicationSettings))                };            }        }        }
}

