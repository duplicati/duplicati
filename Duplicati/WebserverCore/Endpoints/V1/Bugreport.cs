using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Bugreport : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/bugreport/{reportid}", ([FromServices] Connection connection, [FromServices] IHttpContextAccessor httpContextAccessor, [FromRoute] long reportid) =>
        {
            var tf = connection.GetTempFiles().FirstOrDefault(x => x.ID == reportid);
            if (tf == null)
                throw new NotFoundException("Invalid or missing bugreport id");

            if (!File.Exists(tf.Path))
                throw new NotFoundException("File is missing");

            var response = httpContextAccessor.HttpContext!.Response;
            var filename = "bugreport.zip";
            using (var fs = File.OpenRead(tf.Path))
            {
                response.ContentLength = fs.Length;
                response.ContentType = "application/octet-stream";
                response.Headers.Append("Content-Disposition", $"attachment; filename={filename}");
                fs.CopyTo(response.Body);
            }
        })
        .RequireAuthorization();
    }

}
