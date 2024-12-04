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
        X509Certificate2Collection certificate;
        try
        {
            // Load certificate first, to give better error message if it is invalid
            certificate = Library.Utility.Utility.LoadPfxCertificate(Convert.FromBase64String(input.Certificate), input.Password);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "InvalidCertificate", ex, $"Invalid certificate: {ex.Message}");
            throw new BadRequestException($"Invalid certificate: {ex.Message}");
        }

        try
        {
            connection.ApplicationSettings.ServerSSLCertificate = certificate;
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FailedToUpdateCertificate", ex, $"Failed to update certificate: {ex.Message}");
            throw new BadRequestException($"Failed to update certificate: {ex.Message}");
        }
    }
}
