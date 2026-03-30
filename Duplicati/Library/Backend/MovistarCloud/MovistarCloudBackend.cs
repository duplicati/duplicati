using Duplicati.Library.Interface;
using Org.BouncyCastle.Asn1.X500;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.MovistarCloud
{
    public sealed class MovistarCloudBackend : IBackend
    {
        public string DisplayName => "Movistar Cloud (Unofficial)";
        public string ProtocolKey => "movistarcloud";
        public string Description => "Unofficial Movistar Cloud backend (MiCloud/Zefiro). Auth via email+password (headless login).";

        // Optional IDynamicModule Key mapping
        public string Key => ProtocolKey;

        public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>
        {                        
            new CommandLineArgument("email", CommandLineArgument.ArgumentType.String,
                "MiCloud account email (login).", null),

            new CommandLineArgument("password", CommandLineArgument.ArgumentType.Password,
                "MiCloud account password.", null),

            new CommandLineArgument("clientID", CommandLineArgument.ArgumentType.String,
                "MiCloud clientID. Get it from a web session on developer tools from the browser.", null),

            new CommandLineArgument("root-folder-path", CommandLineArgument.ArgumentType.String,
                "Ruta donde almacenar el backup (p.ej. /Duplicati/Backups/Equipo).", null),

            new CommandLineArgument("list-limit", CommandLineArgument.ArgumentType.Integer,
                "Max items returned per listing call.", "2000"),

            new CommandLineArgument("http-timeout-seconds", CommandLineArgument.ArgumentType.Integer,
                "HTTP timeout in seconds.", "120"),

            new CommandLineArgument("wait-for-validation", CommandLineArgument.ArgumentType.Boolean,
                "Wait until uploaded file becomes usable (status=U).", "true"),

            new CommandLineArgument("validation-timeout-seconds", CommandLineArgument.ArgumentType.Integer,
                "Max time to wait for server-side upload validation.", "600"),

            new CommandLineArgument("validation-poll-seconds", CommandLineArgument.ArgumentType.Integer,
                "Polling interval for validation status.", "2"),

            new CommandLineArgument("diagnostics", CommandLineArgument.ArgumentType.Boolean,
                "Enable diagnostics output in TestAsync only.", "false"),

            new CommandLineArgument("diagnostics-level", CommandLineArgument.ArgumentType.String,
                "Diagnostics level: basic|trash", "basic"),

            new CommandLineArgument("trash-page-size", CommandLineArgument.ArgumentType.Integer,
                "Items to list from trash when diagnostics-level=trash.", "50")
        };

        private long _rootFolderId;
        private readonly int _listLimit = 2000;

        private readonly bool _waitForValidation = true;
        private readonly int _validationTimeoutSeconds=600;
        private readonly int _validationPollSeconds=2;

        private readonly bool _diagnostics=false;
        private readonly string _diagnosticsLevel = "basic"; // default seguro
        private readonly int _trashPageSize=50;


        // 1) guarda las opciones opcionales en el ctor (si decides soportarlas):
        private readonly long? _rootParentIdOpt;
        private readonly string? _rootFolderNameOpt;

        // Campos (junto con los que ya tienes)
        private string? _rootFolderPathOpt;

        private bool _destinationResolved=false;

        private readonly MovistarCloudApiClient? _client;
        private readonly Dictionary<string, long> _nameToId = new(StringComparer.Ordinal);

        private MovistarCloudApiClient Client
            => _client ?? throw new InvalidOperationException(
            "Backend not initialized. This instance was created using the default constructor for metadata only."
        );

        // Default ctor required by Duplicati loader for metadata. 
        public MovistarCloudBackend() { }

        // Main ctor required by Duplicati loader. 
        public MovistarCloudBackend(string url, Dictionary<string, string> options)
        {
            var email = Require(options, "email");
            var password = Require(options, "password");
            var clientID = Require(options, "clientID");
            //_rootFolderId = long.Parse(Require(options, "root-folder-id"));
            //_rootFolderId = GetInt(options, "root-folder-id",0);
            // en el constructor (url, options):
            //_rootParentIdOpt = options.TryGetValue("root-parent-id", out var rpid) && long.TryParse(rpid, out var pid) ? pid : (long?)null;
            //_rootParentIdOpt = _rootFolderId;
            //_rootFolderNameOpt = options.TryGetValue("root-folder-name", out var rfn) && !string.IsNullOrWhiteSpace(rfn) ? rfn.Trim() : null;

            // En el constructor (url, options), tras parsear otras opciones:
            _rootFolderPathOpt = options.TryGetValue("root-folder-path", out var rfp) && !string.IsNullOrWhiteSpace(rfp) ? rfp.Trim() : null;


            _listLimit = GetInt(options, "list-limit", 2000);
            var timeoutSeconds = GetInt(options, "http-timeout-seconds", 120);

            _waitForValidation = GetBool(options, "wait-for-validation", true);
            _validationTimeoutSeconds = GetInt(options, "validation-timeout-seconds", 600);
            _validationPollSeconds = GetInt(options, "validation-poll-seconds", 2);

            _diagnostics = GetBool(options, "diagnostics", false);
            _diagnosticsLevel = (options.TryGetValue("diagnostics-level", out var dl) && !string.IsNullOrWhiteSpace(dl))
                ? dl.Trim().ToLowerInvariant()
                : "basic";
            _trashPageSize = Math.Clamp(GetInt(options, "trash-page-size", 50), 1, 200);

            _client = new MovistarCloudApiClient(email, password, clientID, timeoutSeconds);
        }

        public IEnumerable<IFileEntry> List()
        {
            var files = Client.WithAutoRelogin(ct => Client.ListFilesAsync(_rootFolderId, _listLimit, ct), CancellationToken.None)
                               .GetAwaiter().GetResult();

            _nameToId.Clear();
            foreach (var f in files)
                _nameToId[f.Name] = f.Id;

            return files.Select(f => new BasicFileEntry(f.Name, f.Size, f.IsFolder, f.LastWriteUtc));
        }


        

        private async Task EnsureDestinationResolvedAsync(CancellationToken ct)
        {
            if (_destinationResolved)
                return;

            if (_rootFolderId > 0)
            {
                await Client.WithAutoRelogin(x => Client.AssertFolderExistsByIdAsync(_rootFolderId, x), ct);
                _destinationResolved = true;
                return;
            }

            if (!string.IsNullOrWhiteSpace(_rootFolderPathOpt))
            {
                var id = await Client.WithAutoRelogin(x => Client.EnsureFolderPathAsync(_rootFolderPathOpt!, x), ct);
                _rootFolderId = id;
                _destinationResolved = true;
                return;
            }
            
            // Sin id ni path: no sabemos dónde guardar
            throw new FolderMissingException("Falta la carpeta destino: especifica root-folder-path.");
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
        {
            await EnsureDestinationResolvedAsync(cancellationToken).ConfigureAwait(false);

            ValidateRemoteName(remotename);

            var upload = await Client.WithAutoRelogin(
                ct => Client.UploadFileAsync(_rootFolderId, remotename, filename, ct),
                cancellationToken).ConfigureAwait(false);

            if (_waitForValidation)
            {
                var deadline = DateTime.UtcNow.AddSeconds(_validationTimeoutSeconds);
                while (DateTime.UtcNow < deadline)
                {
                    var st = await Client.WithAutoRelogin(
                        ct => Client.GetValidationStatusAsync(upload.Id, ct),
                        cancellationToken).ConfigureAwait(false);

                    if (string.Equals(st, "U", StringComparison.OrdinalIgnoreCase))
                        break;

                    await Task.Delay(TimeSpan.FromSeconds(_validationPollSeconds), cancellationToken).ConfigureAwait(false);
                }
            }

            _nameToId[remotename] = upload.Id;
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
        {
            await EnsureDestinationResolvedAsync(cancellationToken).ConfigureAwait(false);

            var id = await ResolveIdByNameAsync(remotename, cancellationToken).ConfigureAwait(false);

            var signedUrl = await Client.WithAutoRelogin(ct => Client.GetDownloadUrlAsync((long)id, ct), cancellationToken)
                                         .ConfigureAwait(false);

            await Client.DownloadToFileAsync(signedUrl, filename, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
        {
            await EnsureDestinationResolvedAsync(cancellationToken).ConfigureAwait(false);

            var id = await ResolveIdByNameAsync(remotename, cancellationToken, allowMissing: true).ConfigureAwait(false);
            if (id == null) return;

            await Client.WithAutoRelogin(ct => Client.SoftDeleteFileAsync(id.Value, ct), cancellationToken)
                         .ConfigureAwait(false);

            _nameToId.Remove(remotename);
        }
        
        public async Task TestAsync(CancellationToken cancellationToken)
        {
            // Caso A: ya tenemos id -> comprobar que existe
            if (_rootFolderId > 0)
            {
                await Client.WithAutoRelogin(
                    ct => Client.AssertFolderExistsByIdAsync(_rootFolderId, ct), // método ligero (abajo en API)
                    cancellationToken
                ).ConfigureAwait(false);
                return;
            }

            // Caso B: no hay id pero sí hay path -> resolver/crear y rellenar _rootFolderId
            if (!string.IsNullOrWhiteSpace(_rootFolderPathOpt))
            {
                var resolvedId = await Client.WithAutoRelogin(
                    ct => Client.EnsureFolderPathAsync(_rootFolderPathOpt!, ct),
                    cancellationToken
                ).ConfigureAwait(false);

                if (resolvedId <= 0)
                    throw new FolderMissingException($"No se pudo resolver ni crear la ruta '{_rootFolderPathOpt}' en Movistar Cloud.");

                _rootFolderId = resolvedId; // Guardamos el id para el resto de operaciones
                return;
            }

            // Caso C: no hay nada -> indicamos falta de carpeta
            throw new FolderMissingException("Falta la carpeta destino: especifica root-folder-path.");

            /*if (_diagnostics)
            {

                var space = await Client.WithAutoRelogin(ct => Client.GetStorageSpaceAsync(ct), cancellationToken)
                                     .ConfigureAwait(false);
                Console.WriteLine($"[MovistarCloud][diag] used={space.Used} free={space.Free} softdeleted={space.SoftDeleted} nolimit={space.NoLimit}");

                if (_diagnosticsLevel == "trash")
                {
                    var trash = await Client.WithAutoRelogin(ct => Client.ListTrashAsync(_trashPageSize, ct), cancellationToken)
                                             .ConfigureAwait(false);
                    Console.WriteLine($"[MovistarCloud][diag] trash entries: {trash.Count}");
                    foreach (var t in trash.Take(10))
                        Console.WriteLine($"  - id={t.Id} name={t.Name} size={t.Size} origin={t.Origin}");
                }
            }*/


        }



        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await EnsureDestinationResolvedAsync(cancellationToken).ConfigureAwait(false);

            var files = await Client.WithAutoRelogin(
                ct => Client.ListFilesAsync(_rootFolderId, _listLimit, ct),
                cancellationToken
            ).ConfigureAwait(false);

            _nameToId.Clear();
            foreach (var f in files)
                _nameToId[f.Name] = f.Id;

            foreach (var f in files)
                yield return new BasicFileEntry(f.Name, f.Size, f.IsFolder, f.LastWriteUtc);

        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
            => Task.FromResult(new[] { "micloud.movistar.es", "upload.micloud.movistar.es" }); 


        public async Task CreateFolderAsync(CancellationToken cancellationToken)
        {          

            if (!string.IsNullOrWhiteSpace(_rootFolderPathOpt))
            {
                var id = await Client.WithAutoRelogin(x => Client.EnsureFolderPathAsync(_rootFolderPathOpt!, x), cancellationToken);
                _rootFolderId = id;
                _destinationResolved = true;
                return;
            }            

            throw new MissingMethodException("CreateFolderAsync no soportado. Proporciona --root-folder-path para permitir creación.");
        }


        public void Dispose() => _client?.Dispose();

        private async Task<long?> ResolveIdByNameAsync(string remotename, CancellationToken ct, bool allowMissing = false)
        {
            await EnsureDestinationResolvedAsync(ct).ConfigureAwait(false);

            if (_nameToId.TryGetValue(remotename, out var id))
                return id;

            var files = await Client.WithAutoRelogin(x => Client.ListFilesAsync(_rootFolderId, _listLimit, x), ct)
                                     .ConfigureAwait(false);
            _nameToId.Clear();
            foreach (var f in files)
                _nameToId[f.Name] = f.Id;

            if (_nameToId.TryGetValue(remotename, out id))
                return id;

            if (allowMissing) return null;
            throw new FileMissingException($"Remote file not found: {remotename}");
        }

        private static string Require(Dictionary<string, string> options, string key)
        {
            if (options.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
            throw new ArgumentException($"Missing required option: {key}");
        }

        private static int GetInt(Dictionary<string, string> options, string key, int defaultValue)
            => (options.TryGetValue(key, out var v) && int.TryParse(v, out var n)) ? n : defaultValue;

        private static bool GetBool(Dictionary<string, string> options, string key, bool defaultValue)
            => (options.TryGetValue(key, out var v) && bool.TryParse(v, out var b)) ? b : defaultValue;

        private static void ValidateRemoteName(string name)
        {
            // Duplicati remote filenames are usually safe; keep strict. 
            foreach (var c in name)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'))
                    throw new ArgumentException($"Unsupported remote filename char '{c}' in '{name}'");
        }

        private sealed class BasicFileEntry : IFileEntry
        {
            public BasicFileEntry(string name, long size, bool isFolder, DateTime lastWriteUtc)
            {
                Name = name;
                Size = size;

                // MiCloud no nos da (de forma fiable) created/lastaccess en el listado estándar,
                // así que usamos lastWrite como fallback consistente.
                LastModification = lastWriteUtc;
                LastAccess = lastWriteUtc;
                Created = lastWriteUtc;

                IsFolder = isFolder;
                IsArchived = false;
            }

            public string Name { get; }
            public long Size { get; }

            public bool IsFolder { get; }

            // Propiedades “clásicas” del IFileEntry 
            public DateTime LastAccess { get; }
            public DateTime LastModification { get; }
                        
            public DateTime Created { get; }
            public bool IsArchived { get; }
        }
    }
}
