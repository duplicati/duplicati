using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Bugreport : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/bugreport/{reportid}", async ([FromServices] Connection connection, [FromServices] IHttpContextAccessor httpContextAccessor, [FromServices] IJWTTokenProvider jWTTokenProvider, [FromRoute] long reportid, [FromQuery] string token, CancellationToken ct) =>
        {
            // Custom authorization check
            var singleOperationToken = jWTTokenProvider.ReadSingleOperationToken(token);
            if (singleOperationToken.Operation != "bugreport")
                throw new UnauthorizedException("Invalid operation");

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
                await fs.CopyToAsync(response.Body, ct).ConfigureAwait(false);
            }
        });
    }

}
