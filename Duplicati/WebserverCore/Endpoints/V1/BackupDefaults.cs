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
