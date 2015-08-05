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
using System;using System.Collections.Generic;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public class LogData : IRESTMethodGET, IRESTMethodDocumented
    {        public void GET(string key, RequestInfo info)        {            if ("poll".Equals(key, StringComparison.InvariantCultureIgnoreCase))            {                var input = info.Request.QueryString;                var level_str = input["level"].Value ?? "";                var id_str = input["id"].Value ?? "";                Library.Logging.LogMessageType level;                long id;                long.TryParse(id_str, out id);                Enum.TryParse(level_str, true, out level);                info.OutputOK(Program.LogHandler.AfterID(id, level));            }            else            {                List<Dictionary<string, object>> res = null;                Program.DataConnection.ExecuteWithCommand(x =>                {                    res = DumpTable(x, "ErrorLog", "Timestamp", info.Request.QueryString["offset"].Value, info.Request.QueryString["pagesize"].Value);                });                info.OutputOK(res);            }        }        public static List<Dictionary<string, object>> DumpTable(System.Data.IDbCommand cmd, string tablename, string pagingfield, string offset_str, string pagesize_str)        {            var result = new List<Dictionary<string, object>>();            long pagesize;            if (!long.TryParse(pagesize_str, out pagesize))                pagesize = 100;            pagesize = Math.Max(10, Math.Min(500, pagesize));            cmd.CommandText = "SELECT * FROM \"" + tablename + "\"";            long offset = 0;            if (!string.IsNullOrWhiteSpace(offset_str) && long.TryParse(offset_str, out offset) && !string.IsNullOrEmpty(pagingfield))            {                var p = cmd.CreateParameter();                p.Value = offset;                cmd.Parameters.Add(p);                cmd.CommandText += " WHERE \"" + pagingfield + "\" < ?";            }            if (!string.IsNullOrEmpty(pagingfield))                cmd.CommandText += " ORDER BY \"" + pagingfield + "\" DESC";            cmd.CommandText += " LIMIT " + pagesize.ToString();            using(var rd = cmd.ExecuteReader())            {                var names = new List<string>();                for(var i = 0; i < rd.FieldCount; i++)                    names.Add(rd.GetName(i));                while (rd.Read())                {                    var dict = new Dictionary<string, object>();                    for(int i = 0; i < names.Count; i++)                        dict[names[i]] = rd.GetValue(i);                    result.Add(dict);                                                    }            }            return result;        }        public string Description { get { return "Retrieves system log data"; } }        public IEnumerable<KeyValuePair<string, Type>> Types        {            get            {                return new KeyValuePair<string, Type>[] {                    new KeyValuePair<string, Type>(HttpServer.Method.Get, typeof(Dictionary<string, string>[])),                };            }        }
    }
}

