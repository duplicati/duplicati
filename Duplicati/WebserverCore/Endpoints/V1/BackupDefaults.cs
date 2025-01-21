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
using Duplicati.Library.Common.IO;
using Duplicati.Library.RestAPI;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class BackupDefaults : IEndpointV1
{
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<BackupDefaults>();

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/backupdefaults", ([FromServices] Connection connection, [FromServices] IHttpContextAccessor http) =>
        {
            http.HttpContext!.Response.ContentType = "application/json";
            return Execute(connection);
        })
        .RequireAuthorization();
    }

    private static string Execute(Connection connection)
    {
        // TODO: Rewrite to not use Newtonsoft.Json when possible
        // https://github.com/dotnet/runtime/issues/31433

        // Start with a scratch object
        var o = new Newtonsoft.Json.Linq.JObject
        {
            {
                "ApplicationOptions",
                new Newtonsoft.Json.Linq.JArray(
                    connection.Settings.Select(n => Newtonsoft.Json.Linq.JObject.FromObject(n))
                )
            }
        };

        try
        {
            // Add built-in defaults
            Newtonsoft.Json.Linq.JObject? n;
            using (var s = new StreamReader(typeof(FIXMEGlobal).Assembly.GetManifestResourceStream(typeof(FIXMEGlobal), "newbackup.json")!))
                n = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(s.ReadToEnd())!;

            o.Merge(n);
        }
        catch (Exception e)
        {
            Library.Logging.Log.WriteErrorMessage(LOGTAG, "BackupDefaultsError", e, "Failed to locate embeded backup defaults");
        }

        try
        {
            // Add install defaults/overrides, if present
            var path = SystemIO.IO_OS.PathCombine(Library.AutoUpdater.UpdaterManager.INSTALLATIONDIR, "newbackup.json");
            if (File.Exists(path))
            {
                Newtonsoft.Json.Linq.JObject n;
                n = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText(path))!;

                o.Merge(n);
            }
        }
        catch (Exception e)
        {
            Library.Logging.Log.WriteErrorMessage(LOGTAG, "BackupDefaultsError", e, "Failed to process newbackup.json");
        }

        return o.ToString();

    }
}
