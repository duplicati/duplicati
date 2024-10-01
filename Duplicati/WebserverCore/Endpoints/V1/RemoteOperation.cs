using Duplicati.Library.Interface;
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
            var path = Library.Main.DatabaseLocator.GetDatabasePath(uri, null, false, false);
            return new Dto.GetDbPathDto(!string.IsNullOrWhiteSpace(path), path);
        }

        private static Dictionary<string, string> ParseUrlOptions(Library.Utility.Uri uri)
        {
            var qp = uri.QueryParameters;

            var opts = Runner.GetCommonOptions();
            foreach (var k in qp.Keys.Cast<string>())
                opts[k] = qp[k];

            return opts;
        }

        private static IEnumerable<IGenericModule> ConfigureModules(IDictionary<string, string> opts)
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

        private static TupleDisposeWrapper GetBackend(string url)
        {
            var uri = new Library.Utility.Uri(url);
            var opts = ParseUrlOptions(uri);
            var modules = ConfigureModules(opts);
            var backend = Library.DynamicLoader.BackendLoader.GetBackend(url, new Dictionary<string, string>());
            return new TupleDisposeWrapper(backend, modules);
        }

        private static async Task ExecuteTest(string url, bool autoCreate, CancellationToken cancelToken)
        {
            TupleDisposeWrapper? wrapper = null;

            try
            {
                wrapper = GetBackend(url);

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
            finally
            {
                wrapper?.Dispose();
            }
        }


        private static async Task ExecuteCreate(string uri, CancellationToken cancelToken)
        {
            using (var b = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(uri, new Dictionary<string, string>()))
                await b.CreateFolderAsync(cancelToken).ConfigureAwait(false);
        }

    }
}