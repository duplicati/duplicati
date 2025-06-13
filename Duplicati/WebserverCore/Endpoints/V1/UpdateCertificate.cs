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
