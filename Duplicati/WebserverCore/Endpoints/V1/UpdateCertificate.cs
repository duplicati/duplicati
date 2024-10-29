using System.Security.Cryptography.X509Certificates;
using Duplicati.Library.Logging;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class UpdateCertificate : IEndpointV1
{
    private static readonly string LOGTAG = Log.LogTagFromType<UpdateCertificate>();
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/uisettings", ([FromServices] Connection connection, [FromBody] Dto.UpdateCertificateInputDto input)
            => ExecuteUpdate(connection, input))
            .RequireAuthorization();
    }

    private static void ExecuteUpdate(Connection connection, Dto.UpdateCertificateInputDto input)
    {
        X509Certificate2 certificate;
        try
        {
            // The certificate store is broken and only works with files
            using (var tempfile = new Library.Utility.TempFile())
            {
                File.WriteAllBytes(tempfile, Convert.FromBase64String(input.Certificate));
                certificate = new X509Certificate2(tempfile, input.Password, X509KeyStorageFlags.Exportable);
            }
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "InvalidCertificate", ex, $"Invalid certificate: {ex.Message}");
            throw new BadRequestException($"Invalid certificate: {ex.Message}");
        }

        try
        {
            // Read the certificate from temp file, using the supplied password
            connection.ApplicationSettings.SetNewSSLCertificate(certificate);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FailedToUpdateCertificate", ex, $"Failed to update certificate: {ex.Message}");
            throw new BadRequestException($"Failed to update certificate: {ex.Message}");
        }
    }
}
