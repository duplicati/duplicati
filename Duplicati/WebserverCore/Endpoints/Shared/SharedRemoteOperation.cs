using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Endpoints.Shared;

public record TupleDisposeWrapper(IBackend Backend, IEnumerable<IGenericModule> Modules) : IDisposable
{
    public void Dispose()
    {
        Backend.Dispose();
        foreach (var n in Modules)
            if (n is IDisposable disposable)
                disposable.Dispose();
    }
}

public class SharedRemoteOperation
{
    public static Dictionary<string, string?> ParseUrlOptions(Connection connection, Library.Utility.Uri uri)
    {
        var qp = uri.QueryParameters;

        var opts = Runner.GetCommonOptions(connection);
        foreach (var k in qp.Keys.Cast<string>())
            opts[k] = qp[k];

        return opts;
    }

    public static IEnumerable<IGenericModule> ConfigureModules(IDictionary<string, string?> opts)
    {
        var modules = Library.DynamicLoader.GenericLoader.Modules.OfType<IConnectionModule>()
            .Select(x => Library.DynamicLoader.GenericLoader.GetModule(x.Key))
            .WhereNotNull()
            .ToArray();

        foreach (var n in modules)
            n.Configure(opts);

        return modules;
    }

    public static async Task<(string Url, Dictionary<string, string?> Options)> ExpandUrl(Connection connection, IApplicationSettings applicationSettings, string url, string? backupId, CancellationToken cancelToken)
    {
        url = UnmaskUrl(connection, url, backupId);
        var uri = new Library.Utility.Uri(url);
        var opts = ParseUrlOptions(connection, uri);

        var tmp = new[] { uri };
        await SecretProviderHelper.ApplySecretProviderAsync([], tmp, opts, Library.Utility.TempFolder.SystemTempPath, applicationSettings.SecretProvider, cancelToken);
        url = tmp[0].ToString();

        if (uri.Scheme.Equals(Library.Backend.Duplicati.DuplicatiBackend.PROTOCOL, StringComparison.OrdinalIgnoreCase))
            url = Library.Backend.Duplicati.DuplicatiBackend.MergeArgsIntoUrl(
                url,
                connection.ApplicationSettings.RemoteControlStorageApiId,
                connection.ApplicationSettings.RemoteControlStorageApiKey,
                connection.ApplicationSettings.RemoteControlStorageEndpointUrl
            );

        return (url, opts);
    }

    public static async Task<TupleDisposeWrapper> GetBackend(Connection connection, IApplicationSettings applicationSettings, string url, string? backupId, CancellationToken cancelToken)
    {
        (url, var opts) = await ExpandUrl(connection, applicationSettings, url, backupId, cancelToken);
        var modules = ConfigureModules(opts);
        var backend = Library.DynamicLoader.BackendLoader.GetBackend(url, opts);
        return new TupleDisposeWrapper(backend, modules);
    }

    public static Exception GetInnerException<T>(Exception ex) where T : Exception
    {
        var original = ex;
        while (true)
        {
            if (ex == null)
                return original;

            if (ex is T)
                return (T)ex;

            if (ex.InnerException == null)
                throw new ArgumentNullException(nameof(ex.InnerException));

            ex = ex.InnerException;
        }
    }

    private static string UnmaskUrl(Connection connection, string maskedurl, string? backupId)
    {
        var previousUrl = !string.IsNullOrWhiteSpace(backupId) ? connection.GetBackup(backupId)?.TargetURL : null;
        var unmasked = string.IsNullOrWhiteSpace(previousUrl)
            ? maskedurl
            : QuerystringMasking.Unmask(maskedurl, previousUrl);

        if (Connection.UrlContainsPasswordPlaceholder(unmasked))
            throw new ArgumentException("Unmasked URL contains password placeholder");

        return unmasked;
    }
}
