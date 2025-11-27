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

using Duplicati.Library.Interface;
using Duplicati.Library.Main.Volumes;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto.V2;
using Duplicati.WebserverCore.Endpoints.Shared;
using Microsoft.AspNetCore.Mvc;
using InvalidCertificateException = Duplicati.Library.Utility.SslCertificateValidator.InvalidCertificateException;

namespace Duplicati.WebserverCore.Endpoints.V2.Backup;

public class DestinationVerify : IEndpointV2
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/destination/test", ([FromServices] Connection connection, [FromServices] IApplicationSettings applicationSettings, [FromBody] Dto.V2.DestinationTestRequestDto input, CancellationToken cancelToken)
            => ExecuteTest(connection, applicationSettings, input, cancelToken))
            .RequireAuthorization();
    }

    private static async Task<DestinationTestResponseDto> ExecuteTest(Connection connection, IApplicationSettings applicationSettings, DestinationTestRequestDto input, CancellationToken cancelToken)
    {
        TupleDisposeWrapper? wrapper = null;

        try
        {
            var url = SharedRemoteOperation.UnmaskUrl(connection, input.DestinationUrl, input.BackupId);
            wrapper = await SharedRemoteOperation.GetBackend(connection, applicationSettings, url, cancelToken);

            using (var b = wrapper.Backend)
            {
                try { await b.TestAsync(cancelToken).ConfigureAwait(false); }
                catch (Exception ex) when (SharedRemoteOperation.GetInnerException<FolderMissingException>(ex) is FolderMissingException)
                {
                    if (!input.AutoCreate)
                        throw;

                    await b.CreateFolderAsync(cancelToken).ConfigureAwait(false);
                    await b.TestAsync(cancelToken).ConfigureAwait(false);
                }

                var anyFiles = false;
                var anyBackups = false;
                var anyEncryptedFiles = false;
                await foreach (var f in b.ListAsync(cancelToken).ConfigureAwait(false))
                {
                    if (f.IsFolder)
                        continue;

                    anyFiles = true;
                    var parsed = VolumeBase.ParseFilename(f.Name);
                    if (parsed != null)
                    {
                        anyBackups = true;
                        anyEncryptedFiles = !string.IsNullOrWhiteSpace(parsed.EncryptionModule);
                        break;
                    }
                }

                return DestinationTestResponseDto.Create(
                    anyFiles,
                    anyBackups,
                    anyEncryptedFiles
                );

            }
        }
        catch (Exception ex) when (SharedRemoteOperation.GetInnerException<FolderMissingException>(ex) is FolderMissingException)
        {
            if (input.AutoCreate)
                return DestinationTestResponseDto.Failure(
                    "Failed to create folder",
                    "error-creating-folder",
                    folderExists: false,
                    afterConnect: ex is TestAfterConnectException
                );

            return DestinationTestResponseDto.Failure(
                "Folder does not exist",
                "missing-folder",
                folderExists: false,
                afterConnect: ex is TestAfterConnectException
            );
        }
        catch (Exception ex) when (SharedRemoteOperation.GetInnerException<InvalidCertificateException>(ex) is InvalidCertificateException icex)
        {
            if (string.IsNullOrWhiteSpace(icex.Certificate))
                return DestinationTestResponseDto.Failure(
                    icex.Message,
                    "CertError",
                    afterConnect: ex is TestAfterConnectException
                );

            return DestinationTestResponseDto.Failure(
                "Host certificate error",
                "incorrect-cert",
                afterConnect: ex is TestAfterConnectException,
                hostCertificate: icex.Certificate
            );
        }
        catch (Exception ex) when (SharedRemoteOperation.GetInnerException<Library.Utility.HostKeyException>(ex) is Library.Utility.HostKeyException hex)
        {
            if (string.IsNullOrWhiteSpace(hex.ReportedHostKey))
                return DestinationTestResponseDto.Failure(
                    hex.Message,
                    "KeyError",
                    afterConnect: ex is TestAfterConnectException
                );

            return DestinationTestResponseDto.Failure(
                "Host key error",
                "incorrect-host-key",
                afterConnect: ex is TestAfterConnectException,
                reportedHostKey: hex.ReportedHostKey,
                acceptedHostKey: hex.AcceptedHostKey
            );
        }
        catch (TestAfterConnectException tcex)
        {
            return DestinationTestResponseDto.Failure(
                tcex.Message,
                tcex.HelpID,
                afterConnect: true
            );
        }
        catch (UserInformationException uex)
        {
            return DestinationTestResponseDto.Failure(
                uex.Message,
                uex.HelpID
            );
        }
        catch (Exception ex)
        {
            return DestinationTestResponseDto.Failure(
                ex.Message,
                "error"
            );
        }
        finally
        {
            wrapper?.Dispose();
        }
    }
}