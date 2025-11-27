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
using Duplicati.Library.Main;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Endpoints.Shared;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;
using InvalidCertificateException = Duplicati.Library.Utility.SslCertificateValidator.InvalidCertificateException;

namespace Duplicati.WebserverCore.Endpoints.V1
{
    public class RemoteOperation : IEndpointV1
    {
        private record RemoteOperationInput(string path, string? backupId);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/remoteoperation/dbpath", ([FromBody] RemoteOperationInput input)
                => ExecuteDbPath(input.path))
                .RequireAuthorization();

            group.MapPost("/remoteoperation/test", ([FromServices] Connection connection, [FromServices] IApplicationSettings applicationSettings, [FromQuery] bool? autocreate, [FromBody] RemoteOperationInput input, CancellationToken cancelToken)
                => ExecuteTest(connection, applicationSettings, input.path, input.backupId, autocreate ?? false, cancelToken))
                .RequireAuthorization();

            group.MapPost("/remoteoperation/create", ([FromServices] Connection connection, [FromBody] RemoteOperationInput input, CancellationToken cancelToken)
                => ExecuteCreate(connection, input.path, input.backupId, cancelToken))
                .RequireAuthorization();
        }

        private static Dto.GetDbPathDto ExecuteDbPath(string uri)
        {
            var path = CLIDatabaseLocator.GetDatabasePathForCLI(uri, null, false, false);
            return new Dto.GetDbPathDto(!string.IsNullOrWhiteSpace(path), path);
        }

        private static async Task ExecuteTest(Connection connection, IApplicationSettings applicationSettings, string maskedurl, string? backupId, bool autoCreate, CancellationToken cancelToken)
        {
            TupleDisposeWrapper? wrapper = null;

            try
            {
                var url = SharedRemoteOperation.UnmaskUrl(connection, maskedurl, backupId);
                wrapper = await SharedRemoteOperation.GetBackend(connection, applicationSettings, url, cancelToken);

                using (var b = wrapper.Backend)
                {
                    try { await b.TestAsync(cancelToken).ConfigureAwait(false); }
                    catch (Exception ex) when (SharedRemoteOperation.GetInnerException<FolderMissingException>(ex) is FolderMissingException)
                    {
                        if (!autoCreate)
                            throw;

                        await b.CreateFolderAsync(cancelToken).ConfigureAwait(false);
                        await b.TestAsync(cancelToken).ConfigureAwait(false);
                    }

                    return;
                }
            }
            catch (Exception ex) when (SharedRemoteOperation.GetInnerException<FolderMissingException>(ex) is FolderMissingException)
            {
                if (autoCreate)
                    throw new ServerErrorException("error-creating-folder");
                throw new ServerErrorException("missing-folder");
            }
            catch (Exception ex) when (SharedRemoteOperation.GetInnerException<InvalidCertificateException>(ex) is InvalidCertificateException icex)
            {
                if (string.IsNullOrWhiteSpace(icex.Certificate))
                    throw new ServerErrorException(icex.Message);
                else
                    throw new ServerErrorException("incorrect-cert:" + icex.Certificate);
            }
            catch (Exception ex) when (SharedRemoteOperation.GetInnerException<Library.Utility.HostKeyException>(ex) is Library.Utility.HostKeyException hex)
            {
                if (string.IsNullOrWhiteSpace(hex.ReportedHostKey))
                    throw new ServerErrorException(hex.Message);
                else
                {
                    throw new ServerErrorException($@"incorrect-host-key:""{hex.ReportedHostKey}"", accepted-host-key:""{hex.AcceptedHostKey}""");
                }
            }
            catch (UserInformationException uex)
            {
                throw new ServerErrorException($@"error-id:{uex.HelpID}, user-information:{uex.Message}");
            }
            catch (Exception ex)
            {
                throw new ServerErrorException(ex.Message);
            }
            finally
            {
                wrapper?.Dispose();
            }
        }


        private static async Task ExecuteCreate(Connection connection, string maskedurl, string? backupId, CancellationToken cancelToken)
        {
            try
            {
                var url = SharedRemoteOperation.UnmaskUrl(connection, maskedurl, backupId);
                using (var b = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(url, new Dictionary<string, string>()))
                    await b.CreateFolderAsync(cancelToken).ConfigureAwait(false);
            }
            catch (UserInformationException uex)
            {
                throw new ServerErrorException($@"error-id:{uex.HelpID}, user-information:{uex.Message}");
            }
            catch (Exception ex)
            {
                throw new ServerErrorException(ex.Message);
            }
        }

    }
}