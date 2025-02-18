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
using Duplicati.Library.RestAPI;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1
{
    public class RemoteOperation : IEndpointV1
    {
        private record RemoteOperationInput(string path);

        public static void Map(RouteGroupBuilder group)
        {
            group.MapPost("/remoteoperation/dbpath", ([FromBody] RemoteOperationInput input)
                => ExecuteDbPath(input.path))
                .RequireAuthorization();

            group.MapPost("/remoteoperation/test", ([FromQuery] bool? autocreate, [FromBody] RemoteOperationInput input, CancellationToken cancelToken)
                => ExecuteTest(input.path, autocreate ?? false, cancelToken))
                .RequireAuthorization();

            group.MapPost("/remoteoperation/create", ([FromBody] RemoteOperationInput input, CancellationToken cancelToken)
                => ExecuteCreate(input.path, cancelToken))
                .RequireAuthorization();
        }

        private static Dto.GetDbPathDto ExecuteDbPath(string uri)
        {
            var path = CLIDatabaseLocator.GetDatabasePathForCLI(uri, null, false, false);
            return new Dto.GetDbPathDto(!string.IsNullOrWhiteSpace(path), path);
        }

        private static Dictionary<string, string?> ParseUrlOptions(Library.Utility.Uri uri)
        {
            var qp = uri.QueryParameters;

            var opts = Runner.GetCommonOptions();
            foreach (var k in qp.Keys.Cast<string>())
                opts[k] = qp[k];

            return opts;
        }

        private static IEnumerable<IGenericModule> ConfigureModules(IDictionary<string, string?> opts)
        {
            // TODO: This works because the generic modules are implemented
            // with pre .NetCore logic, using static methods
            // The modules are created to allow multipe dispose,
            // which violates the .Net patterns
            var modules = Library.DynamicLoader.GenericLoader.Modules.OfType<IConnectionModule>().ToArray();
            foreach (var n in modules)
                n.Configure(opts);

            return modules;
        }
        private record TupleDisposeWrapper(IBackend Backend, IEnumerable<IGenericModule> Modules) : IDisposable
        {
            public void Dispose()
            {
                Backend.Dispose();
                foreach (var n in Modules)
                    if (n is IDisposable disposable)
                        disposable.Dispose();
            }
        }

        private static async Task<TupleDisposeWrapper> GetBackend(string url, CancellationToken cancelToken)
        {
            var uri = new Library.Utility.Uri(url);
            var opts = ParseUrlOptions(uri);

            var tmp = new[] { uri };
            await SecretProviderHelper.ApplySecretProviderAsync([], tmp, opts, Library.Utility.TempFolder.SystemTempPath, FIXMEGlobal.SecretProvider, cancelToken);
            url = tmp[0].ToString();

            var modules = ConfigureModules(opts);
            var backend = Library.DynamicLoader.BackendLoader.GetBackend(url, new Dictionary<string, string>());
            return new TupleDisposeWrapper(backend, modules);
        }

        private static async Task ExecuteTest(string url, bool autoCreate, CancellationToken cancelToken)
        {
            TupleDisposeWrapper? wrapper = null;

            try
            {
                wrapper = await GetBackend(url, cancelToken);

                using (var b = wrapper.Backend)
                {
                    try { await b.TestAsync(cancelToken).ConfigureAwait(false); }
                    catch (FolderMissingException)
                    {
                        if (!autoCreate)
                            throw;

                        await b.CreateFolderAsync(cancelToken).ConfigureAwait(false);
                        await b.TestAsync(cancelToken).ConfigureAwait(false);
                    }

                    return;
                }
            }
            // TODO: These should be wrapped in a JSON response, possibly with 200 status code
            catch (FolderMissingException)
            {
                if (autoCreate)
                    throw new ServerErrorException("error-creating-folder");
                throw new ServerErrorException("missing-folder");
            }
            catch (Library.Utility.SslCertificateValidator.InvalidCertificateException icex)
            {
                if (string.IsNullOrWhiteSpace(icex.Certificate))
                    throw new ServerErrorException(icex.Message);
                else
                    throw new ServerErrorException("incorrect-cert:" + icex.Certificate);
            }
            catch (Library.Utility.HostKeyException hex)
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


        private static async Task ExecuteCreate(string uri, CancellationToken cancelToken)
        {
            try
            {
                using (var b = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(uri, new Dictionary<string, string>()))
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