using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Dropbox
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class Dropbox : IBackend, IBackendPagination
    {
        private const string AUTHID_OPTION = "authid";

        private readonly string m_accesToken;
        private readonly string m_path;
        private readonly DropboxHelper dbx;

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Dropbox()
        {
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Dropbox(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_path = Library.Utility.Uri.UrlDecode(uri.HostAndPath);
            if (m_path.Length != 0 && !m_path.StartsWith("/", StringComparison.Ordinal))
                m_path = "/" + m_path;

            if (m_path.EndsWith("/", StringComparison.Ordinal))
                m_path = m_path.Substring(0, m_path.Length - 1);

            if (options.ContainsKey(AUTHID_OPTION))
                m_accesToken = options[AUTHID_OPTION];

            dbx = new DropboxHelper(m_accesToken);
        }

        public void Dispose()
        {
            // do nothing
        }

        public string DisplayName
        {
            get { return Strings.Dropbox.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "dropbox"; }
        }

        private IFileEntry ParseEntry(MetaData md)
        {
            var ife = new FileEntry(md.name);
            if (md.IsFile)
            {
                ife.IsFolder = false;
                ife.Size = (long)md.size;
            }
            else
            {
                ife.IsFolder = true;
            }

            try { ife.LastModification = ife.LastAccess = DateTime.Parse(md.server_modified).ToUniversalTime(); }
            catch { }

            return ife;
        }

        private async Task<T> HandleListExceptionsAsync<T>(Func<Task<T>> func)
        {
            try
            {
                return await func();
            }
            catch (DropboxException de)
            {
                if (de.errorJSON["error"][".tag"].ToString() == "path" && de.errorJSON["error"]["path"][".tag"].ToString() == "not_found")
                    throw new FolderMissingException();

                throw;
            }
        }

        public async IAsyncEnumerable<IFileEntry> ListEnumerableAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancelToken)
        {
            var lfr = await HandleListExceptionsAsync(() => dbx.ListFilesAsync(m_path, cancelToken));
              
            foreach (var md in lfr.entries)
                yield return ParseEntry(md);

            while (lfr.has_more)
            {
                lfr = await HandleListExceptionsAsync(() => dbx.ListFilesContinueAsync(lfr.cursor, cancelToken));
                foreach (var md in lfr.entries)
                    yield return ParseEntry(md);
            }
        }

        public Task<IList<IFileEntry>> ListAsync(CancellationToken cancelToken)
            => this.CondensePaginatedListAsync(cancelToken);

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                string path = String.Format("{0}/{1}", m_path, remotename);
                await dbx.DeleteAsync(path, cancelToken);
            }
            catch (DropboxException)
            {
                // we can catch some events here and convert them to Duplicati exceptions
                throw;
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION, CommandLineArgument.ArgumentType.Password, Strings.Dropbox.AuthidShort, Strings.Dropbox.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("dropbox"))),
                });
            }
        }

        public string Description { get { return Strings.Dropbox.Description; } }

        public string[] DNSName
        {
            get { return WebApi.Dropbox.Hosts(); }
        }

        public bool SupportsStreaming => true;

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            try
            {
                await dbx.CreateFolderAsync(m_path, cancelToken);
            }
            catch (DropboxException de)
            {

                if (de.errorJSON["error"][".tag"].ToString() == "path" && de.errorJSON["error"]["path"][".tag"].ToString() == "conflict")
                    throw new FolderAreadyExistedException();
                throw;
            }
        }

        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                string path = $"{m_path}/{remotename}";
                await dbx.UploadFileAsync(path, stream, cancelToken);
            }
            catch (DropboxException)
            {
                // we can catch some events here and convert them to Duplicati exceptions
                throw;
            }
        }

        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                string path = string.Format("{0}/{1}", m_path, remotename);
                await dbx.DownloadFileAsync(path, stream, cancelToken);
            }
            catch (DropboxException)
            {
                // we can catch some events here and convert them to Duplicati exceptions
                throw;
            }
        }
    }
}
