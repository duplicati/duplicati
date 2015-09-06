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
using System;using System.Linq;using System.Text;using Newtonsoft.Json;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class Help : IRESTMethodGET
    {        public void GET(string key, RequestInfo info)        {            var sb = new StringBuilder();            if (string.IsNullOrWhiteSpace(key))            {                foreach(var m in RESTHandler.Modules.Keys.OrderBy(x => x))                {                                        var mod = RESTHandler.Modules[m];                    if (mod == this)                        continue;                    var desc = mod.GetType().Name;                    if (mod is IRESTMethodDocumented)                        desc = ((IRESTMethodDocumented)mod).Description;                    sb.AppendFormat(ITEM_TEMPLATE, RESTHandler.API_URI_PATH, m, mod.GetType().Name, desc);                }                var data = Encoding.UTF8.GetBytes(string.Format(TEMPLATE, "API Information", "", sb.ToString()));                info.Response.ContentType = "text/html";                info.Response.ContentLength = data.Length;                info.Response.Body.Write(data, 0, data.Length);                info.Response.Send();            }            else            {                IRESTMethod m;                RESTHandler.Modules.TryGetValue(key, out m);                if (m == null)                {                    info.Response.Status = System.Net.HttpStatusCode.NotFound;                    info.Response.Reason = "Module not found";                }                else                {                    var desc = "";                    if (m is IRESTMethodDocumented)                    {                        var doc = m as IRESTMethodDocumented;                        desc = doc.Description;                        foreach(var t in doc.Types)                            sb.AppendFormat(METHOD_TEMPLATE, t.Key, JsonConvert.SerializeObject(t.Value)); //TODO: Format the type                    }                    var data = Encoding.UTF8.GetBytes(string.Format(TEMPLATE, m.GetType().Name, desc, sb.ToString()));                    info.Response.ContentType = "text/html";                    info.Response.ContentLength = data.Length;                    info.Response.Body.Write(data, 0, data.Length);                    info.Response.Send();                                   }            }        }        private const string TEMPLATE = @"<html><head><title>{0}</title></head><body><h1>{0}</h1><p>{1}</p><ul>{2}</ul></body>";        private const string ITEM_TEMPLATE = @"<li><a href=""{0}/help/{1}"">{2}</a>: {3}</li>";        private const string METHOD_TEMPLATE = @"<b>{0}:</b><br><code>{1}</code><br>";    }
}

