// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Library.RestAPI;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class LogData : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/logdata/poll", ([FromServices] Connection connection, [FromQuery] Library.Logging.LogMessageType level, [FromQuery] long id, [FromQuery] long? offset, [FromQuery] int? pagesize)
            => ExecuteLogPoll(level, id, offset ?? 0, pagesize ?? 100))
            .RequireAuthorization();

        group.MapGet("/logdata/log", ([FromServices] Connection connection, [FromQuery] long? offset, [FromQuery] int? pagesize)
            => ExecuteGetLog(connection, offset, pagesize ?? 100))
            .RequireAuthorization();
    }

    private static Dto.LogEntry[] ExecuteLogPoll(Library.Logging.LogMessageType level, long id, long offset, int pagesize)
    {
        pagesize = Math.Max(1, Math.Min(500, pagesize));
        return FIXMEGlobal.LogHandler.AfterID(id, level, pagesize).Select(x => Dto.LogEntry.FromInternalEntry(x)).ToArray();
    }

    private static List<Dictionary<string, object>>? ExecuteGetLog(Connection connection, long? offset, long pagesize)
    {
        List<Dictionary<string, object>>? res = null;
        connection.ExecuteWithCommand(x =>
        {
            res = DumpTable(x, "ErrorLog", "Timestamp", offset, pagesize);
        });

        return res;
    }

    public static List<Dictionary<string, object>> DumpTable(System.Data.IDbCommand cmd, string tablename, string pagingfield, long? offset, long pagesize)
    {
        var result = new List<Dictionary<string, object>>();

        pagesize = Math.Max(10, Math.Min(500, pagesize));

        cmd.CommandText = "SELECT * FROM \"" + tablename + "\"";
        if (!string.IsNullOrEmpty(pagingfield) && offset != null)
        {
            var p = cmd.CreateParameter();
            p.Value = offset.Value;
            cmd.Parameters.Add(p);

            cmd.CommandText += " WHERE \"" + pagingfield + "\" < ?";
        }

        if (!string.IsNullOrEmpty(pagingfield))
            cmd.CommandText += " ORDER BY \"" + pagingfield + "\" DESC";
        cmd.CommandText += " LIMIT " + pagesize.ToString();

        using (var rd = cmd.ExecuteReader())
        {
            var names = new List<string>();
            for (var i = 0; i < rd.FieldCount; i++)
                names.Add(rd.GetName(i));

            while (rd.Read())
            {
                var dict = new Dictionary<string, object>();
                for (int i = 0; i < names.Count; i++)
                    dict[names[i]] = rd.GetValue(i);

                result.Add(dict);
            }
        }

        return result;
    }
}
