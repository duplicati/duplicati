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
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend.MovistarCloud;

/// <summary>
/// The Movistar Cloud backend (MiCloud/Zefiro)
/// </summary>
public sealed class MovistarCloudBackend : IBackend
{
    /// <summary>
    /// The log tag for this backend
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<MovistarCloudBackend>();

    /// <summary>
    /// The email option name
    /// </summary>
    private const string EmailOption = "email";
    /// <summary>
    /// The password option name
    /// </summary>
    private const string PasswordOption = "password";
    /// <summary>
    /// The client ID option name
    /// </summary>
    private const string ClientIdOption = "clientID";
    /// <summary>
    /// The root folder path option name
    /// </summary>
    private const string RootFolderPathOption = "root-folder-path";
    /// <summary>
    /// The list limit option name
    /// </summary>
    private const string ListLimitOption = "list-limit";
    /// <summary>
    /// The wait for validation option name
    /// </summary>
    private const string WaitForValidationOption = "wait-for-validation";
    /// <summary>
    /// The validation timeout option name
    /// </summary>
    private const string ValidationTimeoutOption = "validation-timeout";
    /// <summary>
    /// The validation poll interval option name
    /// </summary>
    private const string ValidationPollIntervalOption = "validation-poll-interval";
    /// <summary>
    /// The diagnostics option name
    /// </summary>
    private const string DiagnosticsOption = "diagnostics";
    /// <summary>
    /// The diagnostics level option name
    /// </summary>
    private const string DiagnosticsLevelOption = "diagnostics-level";
    /// <summary>
    /// The trash page size option name
    /// </summary>
    private const string TrashPageSizeOption = "trash-page-size";

    /// <summary>
    /// The default list limit value
    /// </summary>
    private const int DefaultListLimit = 2000;
    /// <summary>
    /// The default wait for validation value
    /// </summary>
    private const bool DefaultWaitForValidation = true;
    /// <summary>
    /// The default validation timeout value
    /// </summary>
    private const string DefaultValidationTimeout = "10m";
    /// <summary>
    /// The default validation poll interval value
    /// </summary>
    private const string DefaultValidationPollInterval = "2s";
    /// <summary>
    /// The default diagnostics enabled value
    /// </summary>
    private const bool DefaultDiagnostics = false;
    /// <summary>
    /// The default diagnostics level value
    /// </summary>
    private const DiagnosticsLevel DefaultDiagnosticsLevel = DiagnosticsLevel.Basic;
    /// <summary>
    /// The default trash page size value
    /// </summary>
    private const int DefaultTrashPageSize = 50;

    /// <summary>
    /// The diagnostics level for logging
    /// </summary>
    private enum DiagnosticsLevel
    {
        /// <summary>
        /// Basic diagnostics (storage space only)
        /// </summary>
        Basic,
        /// <summary>
        /// Include trash entries in diagnostics
        /// </summary>
        Trash
    }

    /// <summary>
    /// The root folder ID
    /// </summary>
    private long _rootFolderId;
    
    /// <summary>
    /// The maximum number of items to return per listing call
    /// </summary>
    private readonly int _listLimit;
    
    /// <summary>
    /// Whether to wait until uploaded file becomes usable (status=U)
    /// </summary>
    private readonly bool _waitForValidation;
    
    /// <summary>
    /// The maximum time to wait for server-side upload validation
    /// </summary>
    private readonly TimeSpan _validationTimeout;
    
    /// <summary>
    /// The polling interval for validation status
    /// </summary>
    private readonly TimeSpan _validationPollInterval;
    
    /// <summary>
    /// Whether diagnostics logging is enabled
    /// </summary>
    private readonly bool _diagnostics;
    
    /// <summary>
    /// The diagnostics level (basic or trash)
    /// </summary>
    private readonly DiagnosticsLevel _diagnosticsLevel;
    
    /// <summary>
    /// The number of items to list from trash when diagnostics-level=trash
    /// </summary>
    private readonly int _trashPageSize;
    
    /// <summary>
    /// The optional root folder path
    /// </summary>
    private readonly string? _rootFolderPathOpt;
    
    /// <summary>
    /// Whether the destination has been resolved
    /// </summary>
    private bool _destinationResolved;

    /// <summary>
    /// The API client instance
    /// </summary>
    private readonly MovistarCloudApiClient? _client;
    
    /// <summary>
    /// The mapping of file names to IDs
    /// </summary>
    private readonly Dictionary<string, long> _nameToId = new(StringComparer.Ordinal);
    
    /// <summary>
    /// The timeout configuration
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _timeouts;

    /// <summary>
    /// Gets the API client, throwing if not initialized
    /// </summary>
    private MovistarCloudApiClient Client
        => _client ?? throw new InvalidOperationException(
            "Backend not initialized. This instance was created using the default constructor for metadata only."
        );

    /// <inheritdoc/>
    public string DisplayName => Strings.MovistarCloudBackend.DisplayName;
    
    /// <inheritdoc/>
    public string ProtocolKey => "movistarcloud";
    
    /// <inheritdoc/>
    public string Description => Strings.MovistarCloudBackend.Description;

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(EmailOption, CommandLineArgument.ArgumentType.String, Strings.MovistarCloudBackend.EmailShort, Strings.MovistarCloudBackend.EmailLong),
        new CommandLineArgument(PasswordOption, CommandLineArgument.ArgumentType.Password, Strings.MovistarCloudBackend.PasswordShort, Strings.MovistarCloudBackend.PasswordLong),
        new CommandLineArgument(ClientIdOption, CommandLineArgument.ArgumentType.String, Strings.MovistarCloudBackend.ClientIdShort, Strings.MovistarCloudBackend.ClientIdLong),
        new CommandLineArgument(RootFolderPathOption, CommandLineArgument.ArgumentType.String, Strings.MovistarCloudBackend.RootFolderPathShort, Strings.MovistarCloudBackend.RootFolderPathLong),
        new CommandLineArgument(ListLimitOption, CommandLineArgument.ArgumentType.Integer, Strings.MovistarCloudBackend.ListLimitShort, Strings.MovistarCloudBackend.ListLimitLong, DefaultListLimit.ToString()),
        new CommandLineArgument(WaitForValidationOption, CommandLineArgument.ArgumentType.Boolean, Strings.MovistarCloudBackend.WaitForValidationShort, Strings.MovistarCloudBackend.WaitForValidationLong, DefaultWaitForValidation.ToString()),
        new CommandLineArgument(ValidationTimeoutOption, CommandLineArgument.ArgumentType.Timespan, Strings.MovistarCloudBackend.ValidationTimeoutShort, Strings.MovistarCloudBackend.ValidationTimeoutLong, DefaultValidationTimeout),
        new CommandLineArgument(ValidationPollIntervalOption, CommandLineArgument.ArgumentType.Timespan, Strings.MovistarCloudBackend.ValidationPollIntervalShort, Strings.MovistarCloudBackend.ValidationPollIntervalLong, DefaultValidationPollInterval),
        new CommandLineArgument(DiagnosticsOption, CommandLineArgument.ArgumentType.Boolean, Strings.MovistarCloudBackend.DiagnosticsShort, Strings.MovistarCloudBackend.DiagnosticsLong, DefaultDiagnostics.ToString()),
        new CommandLineArgument(DiagnosticsLevelOption, CommandLineArgument.ArgumentType.Enumeration, Strings.MovistarCloudBackend.DiagnosticsLevelShort, Strings.MovistarCloudBackend.DiagnosticsLevelLong, DefaultDiagnosticsLevel.ToString().ToLowerInvariant(), null, ["basic", "trash"]),
        new CommandLineArgument(TrashPageSizeOption, CommandLineArgument.ArgumentType.Integer, Strings.MovistarCloudBackend.TrashPageSizeShort, Strings.MovistarCloudBackend.TrashPageSizeLong, DefaultTrashPageSize.ToString()),
        .. TimeoutOptionsHelper.GetOptions()
    ];

    /// <summary>
    /// Default constructor required by Duplicati loader for metadata.
    /// </summary>
    public MovistarCloudBackend()
    {
        _listLimit = DefaultListLimit;
        _waitForValidation = DefaultWaitForValidation;
        _validationTimeout = Timeparser.ParseTimeSpan(DefaultValidationTimeout);
        _validationPollInterval = Timeparser.ParseTimeSpan(DefaultValidationPollInterval);
        _diagnostics = DefaultDiagnostics;
        _diagnosticsLevel = DefaultDiagnosticsLevel;
        _trashPageSize = DefaultTrashPageSize;
        _timeouts = null!;
        _rootFolderPathOpt = null;
    }

    /// <summary>
    /// Main constructor required by Duplicati loader.
    /// </summary>
    /// <param name="url">The connection URL</param>
    /// <param name="options">The options dictionary</param>
    public MovistarCloudBackend(string url, Dictionary<string, string?> options)
    {
        var email = RequireOption(options, EmailOption);
        var password = RequireOption(options, PasswordOption);
        var clientID = RequireOption(options, ClientIdOption);

        _rootFolderPathOpt = options.GetValueOrDefault(RootFolderPathOption)?.Trim();
        _listLimit = Library.Utility.Utility.ParseIntOption(options, ListLimitOption, DefaultListLimit);
        _waitForValidation = Library.Utility.Utility.ParseBoolOption(options, WaitForValidationOption);
        _validationTimeout = Library.Utility.Utility.ParseTimespanOption(options, ValidationTimeoutOption, DefaultValidationTimeout);
        _validationPollInterval = Library.Utility.Utility.ParseTimespanOption(options, ValidationPollIntervalOption, DefaultValidationPollInterval);
        _diagnostics = Library.Utility.Utility.ParseBoolOption(options, DiagnosticsOption);
        _diagnosticsLevel = Library.Utility.Utility.ParseEnumOption(options, DiagnosticsLevelOption, DefaultDiagnosticsLevel);
        _trashPageSize = Math.Clamp(Library.Utility.Utility.ParseIntOption(options, TrashPageSizeOption, DefaultTrashPageSize), 1, 200);

        _timeouts = TimeoutOptionsHelper.Parse(options);
        _client = MovistarCloudApiClient.CreateAsync(email, password, clientID, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public IEnumerable<IFileEntry> List()
    {
        var files = Client.WithAutoRelogin(ct => Client.ListFilesAsync(_rootFolderId, _listLimit, _timeouts.ListTimeout, ct), CancellationToken.None)
                           .GetAwaiter().GetResult();

        _nameToId.Clear();
        foreach (var f in files)
            _nameToId[f.Name] = f.Id;

        return files.Select(f => new BasicFileEntry(f.Name, f.Size, f.IsFolder, f.LastWriteUtc));
    }

    /// <summary>
    /// Ensures the destination folder is resolved, creating it if necessary.
    /// </summary>
    /// <param name="ct">The cancellation token</param>
    private async Task EnsureDestinationResolvedAsync(CancellationToken ct)
    {
        if (_destinationResolved)
            return;

        if (_rootFolderId > 0)
        {
            await Client.WithAutoRelogin(x => Client.AssertFolderExistsByIdAsync(_rootFolderId, _timeouts.ShortTimeout, x), ct);
            _destinationResolved = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_rootFolderPathOpt))
        {
            var id = await Client.WithAutoRelogin(x => Client.EnsureFolderPathAsync(_rootFolderPathOpt!, _timeouts.ShortTimeout, x), ct);
            _rootFolderId = id;
            _destinationResolved = true;
            return;
        }

        throw new FolderMissingException("Missing destination folder: specify root-folder-path.");
    }

    /// <inheritdoc/>
    public async Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
    {
        await EnsureDestinationResolvedAsync(cancellationToken).ConfigureAwait(false);
        ValidateRemoteName(remotename);

        var upload = await Client.WithAutoRelogin(
            ct => Client.UploadFileAsync(_rootFolderId, remotename, filename, _timeouts.ReadWriteTimeout, ct),
            cancellationToken).ConfigureAwait(false);

        if (_waitForValidation)
        {
            var deadline = DateTime.UtcNow.Add(_validationTimeout);
            while (DateTime.UtcNow < deadline)
            {
                var st = await Client.WithAutoRelogin(
                    ct => Client.GetValidationStatusAsync(upload.Id, _timeouts.ShortTimeout, ct),
                    cancellationToken).ConfigureAwait(false);

                if (string.Equals(st, "U", StringComparison.OrdinalIgnoreCase))
                    break;

                await Task.Delay(_validationPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        _nameToId[remotename] = upload.Id;
    }

    /// <inheritdoc/>
    public async Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
    {
        await EnsureDestinationResolvedAsync(cancellationToken).ConfigureAwait(false);
        var id = await ResolveIdByNameAsync(remotename, cancellationToken).ConfigureAwait(false);

        var signedUrl = await Client.WithAutoRelogin(
            ct => Client.GetDownloadUrlAsync((long)id, _timeouts.ShortTimeout, ct),
            cancellationToken).ConfigureAwait(false);

        await Client.DownloadToFileAsync(signedUrl, filename, _timeouts.ReadWriteTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
    {
        await EnsureDestinationResolvedAsync(cancellationToken).ConfigureAwait(false);
        var id = await ResolveIdByNameAsync(remotename, cancellationToken, allowMissing: true).ConfigureAwait(false);
        if (id == null) return;

        await Client.WithAutoRelogin(
            ct => Client.SoftDeleteFileAsync(id.Value, _timeouts.ShortTimeout, ct),
            cancellationToken).ConfigureAwait(false);

        _nameToId.Remove(remotename);
    }

    /// <inheritdoc/>
    public async Task TestAsync(CancellationToken cancellationToken)
    {
        // Case A: we already have an id -> check that it exists
        if (_rootFolderId > 0)
        {
            await Client.WithAutoRelogin(
                ct => Client.AssertFolderExistsByIdAsync(_rootFolderId, _timeouts.ShortTimeout, ct),
                cancellationToken
            ).ConfigureAwait(false);
        }
        // Case B: no id but there is a path -> resolve/create and fill _rootFolderId
        else if (!string.IsNullOrWhiteSpace(_rootFolderPathOpt))
        {
            var resolvedId = await Client.WithAutoRelogin(
                ct => Client.EnsureFolderPathAsync(_rootFolderPathOpt!, _timeouts.ShortTimeout, ct),
                cancellationToken
            ).ConfigureAwait(false);

            if (resolvedId <= 0)
                throw new FolderMissingException($"Could not resolve or create path '{_rootFolderPathOpt}' in Movistar Cloud.");

            _rootFolderId = resolvedId;
        }
        // Case C: nothing -> indicate missing folder
        else
        {
            throw new FolderMissingException("Missing destination folder: specify root-folder-path.");
        }

        // Diagnostics logging when enabled
        if (_diagnostics)
        {
            await RunDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs diagnostics logging when enabled.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    private async Task RunDiagnosticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var space = await Client.WithAutoRelogin(
                ct => Client.GetStorageSpaceAsync(_timeouts.ListTimeout, ct),
                cancellationToken
            ).ConfigureAwait(false);

            Logging.Log.WriteInformationMessage(LOGTAG, "DiagnosticsStorage",
                "Storage: used={0} free={1} softdeleted={2} nolimit={3}",
                space.Used, space.Free, space.SoftDeleted, space.NoLimit);

            if (_diagnosticsLevel == DiagnosticsLevel.Trash)
            {
                var trash = await Client.WithAutoRelogin(
                    ct => Client.ListTrashAsync(_trashPageSize, _timeouts.ListTimeout, ct),
                    cancellationToken
                ).ConfigureAwait(false);

                Logging.Log.WriteInformationMessage(LOGTAG, "DiagnosticsTrash",
                    "Trash entries: {0}", trash.Count);
                foreach (var t in trash.Take(10))
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "DiagnosticsTrashEntry",
                        "  - id={0} name={1} size={2} origin={3}", t.Id, t.Name, t.Size, t.Origin);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "DiagnosticsFailed", ex, "Diagnostics failed: {0}", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await EnsureDestinationResolvedAsync(cancellationToken).ConfigureAwait(false);

        var files = await Client.WithAutoRelogin(
            ct => Client.ListFilesAsync(_rootFolderId, _listLimit, _timeouts.ListTimeout, ct),
            cancellationToken
        ).ConfigureAwait(false);

        _nameToId.Clear();
        foreach (var f in files)
            _nameToId[f.Name] = f.Id;

        foreach (var f in files)
            yield return new BasicFileEntry(f.Name, f.Size, f.IsFolder, f.LastWriteUtc);
    }

    /// <inheritdoc/>
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        => Task.FromResult(new[] { "micloud.movistar.es", "upload.micloud.movistar.es" });

    /// <inheritdoc/>
    public async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_rootFolderPathOpt))
        {
            var id = await Client.WithAutoRelogin(
                x => Client.EnsureFolderPathAsync(_rootFolderPathOpt!, _timeouts.ShortTimeout, x),
                cancellationToken);
            _rootFolderId = id;
            _destinationResolved = true;
            return;
        }

        throw new MissingMethodException("CreateFolderAsync not supported. Provide --root-folder-path to allow creation.");
    }

    /// <inheritdoc/>
    public void Dispose() => _client?.Dispose();

    /// <summary>
    /// Resolves the file ID by name.
    /// </summary>
    /// <param name="remotename">The remote file name</param>
    /// <param name="ct">The cancellation token</param>
    /// <param name="allowMissing">Whether to allow missing files</param>
    /// <returns>The file ID, or null if allowMissing is true and file not found</returns>
    private async Task<long?> ResolveIdByNameAsync(string remotename, CancellationToken ct, bool allowMissing = false)
    {
        await EnsureDestinationResolvedAsync(ct).ConfigureAwait(false);

        if (_nameToId.TryGetValue(remotename, out var id))
            return id;

        var files = await Client.WithAutoRelogin(
            x => Client.ListFilesAsync(_rootFolderId, _listLimit, _timeouts.ListTimeout, x),
            ct).ConfigureAwait(false);

        _nameToId.Clear();
        foreach (var f in files)
            _nameToId[f.Name] = f.Id;

        if (_nameToId.TryGetValue(remotename, out id))
            return id;

        if (allowMissing) return null;
        throw new FileMissingException($"Remote file not found: {remotename}");
    }

    /// <summary>
    /// Gets a required option value from the options dictionary.
    /// </summary>
    /// <param name="options">The options dictionary</param>
    /// <param name="key">The option key</param>
    /// <returns>The option value</returns>
    /// <exception cref="ArgumentException">Thrown when the option is missing</exception>
    private static string RequireOption(Dictionary<string, string?> options, string key)
    {
        if (options.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            return v.Trim();
        throw new ArgumentException($"Missing required option: {key}");
    }

    /// <summary>
    /// Validates the remote file name for allowed characters.
    /// </summary>
    /// <param name="name">The file name to validate</param>
    /// <exception cref="ArgumentException">Thrown when the name contains invalid characters</exception>
    private static void ValidateRemoteName(string name)
    {
        foreach (var c in name)
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'))
                throw new ArgumentException($"Unsupported remote filename char '{c}' in '{name}'");
    }

    /// <summary>
    /// Basic file entry implementation for Movistar Cloud files.
    /// </summary>
    private sealed class BasicFileEntry : IFileEntry
    {
        /// <summary>
        /// Creates a new file entry.
        /// </summary>
        /// <param name="name">The file name</param>
        /// <param name="size">The file size</param>
        /// <param name="isFolder">Whether this is a folder</param>
        /// <param name="lastWriteUtc">The last write time in UTC</param>
        public BasicFileEntry(string name, long size, bool isFolder, DateTime lastWriteUtc)
        {
            Name = name;
            Size = size;
            LastModification = lastWriteUtc;
            LastAccess = lastWriteUtc;
            Created = lastWriteUtc;
            IsFolder = isFolder;
            IsArchived = false;
        }

        /// <inheritdoc/>
        public string Name { get; }
        
        /// <inheritdoc/>
        public long Size { get; }
        
        /// <inheritdoc/>
        public bool IsFolder { get; }
        
        /// <inheritdoc/>
        public DateTime LastAccess { get; }
        
        /// <inheritdoc/>
        public DateTime LastModification { get; }
        
        /// <inheritdoc/>
        public DateTime Created { get; }
        
        /// <inheritdoc/>
        public bool IsArchived { get; }
    }
}
