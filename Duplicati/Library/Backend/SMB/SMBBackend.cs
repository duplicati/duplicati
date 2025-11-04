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
using Duplicati.Library.Backend.SMB;
using Duplicati.Library.Backend.SMB.Model;
using SMBLibrary;
using Duplicati.Library.SourceProvider;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility.Options;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend;

/// <summary>
/// Native CIFS/SMB Backend implementation
/// </summary>
public class SMBBackend : IStreamingBackend, IFolderEnabledBackend
{
    /// <inheritdoc/>
    public virtual string ProtocolKey => "smb";

    /// <inheritdoc/>
    public virtual string DisplayName => Strings.SMBBackend.DisplayName;

    /// <inheritdoc/>
    public virtual string Description => Strings.SMBBackend.Description;

    /// <inheritdoc/>
    public virtual bool SupportsStreaming => true;

    /// <summary>
    /// Hostname only (no ports or paths) to be used on DNS resolutions.
    /// </summary>
    private string _DnsName;

    /// <summary>
    /// Path separators (both Windows \ and unix /) to be used in path manipulation
    /// </summary>
    private static readonly char[] PATH_SEPARATORS = ['/', '\\'];

    /// <summary>
    /// Cache of parsed connection parameters
    /// </summary>
    private readonly SMBConnectionParameters _connectionParameters;

    /// <summary>
    /// Timeout options to be used in the backend
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _timeouts;

    /// <summary>
    /// Shared connection between all methods to avoid re-authentication
    /// </summary>
    private SMBShareConnection? _sharedConnection;

    /// <summary>
    /// Read buffer size for SMB operations (will be capped automatically by SMB negotiated values)
    /// </summary>
    private const string READ_BUFFER_SIZE_OPTION = "read-buffer-size";

    /// <summary>
    /// Write buffer size for SMB operations (will be capped automatically by SMB negotiated values)
    /// </summary>
    private const string WRITE_BUFFER_SIZE_OPTION = "write-buffer-size";

    /// <summary>
    /// Backend option for controlling the transport (directtcp or netbios)
    /// </summary>
    private const string TRANSPORT_OPTION = "transport";

    /// <summary>
    /// Domain (complementary part of authentication) option
    /// </summary>
    private const string AUTH_DOMAIN_OPTION = "auth-domain";

    /// <summary>
    /// Defines the default transport to be used in CIFS connection
    /// </summary>
    private const string DEFAULT_TRANSPORT = "directtcp";

    /// <summary>
    /// Mapping of transport string to SMBTransportType enum to be used in parsing the option string
    /// </summary>
    private readonly Dictionary<string, SMBTransportType> _transportMap = new()
    {
        ["directtcp"] = SMBTransportType.DirectTCPTransport,
        ["netbios"] = SMBTransportType.NetBiosOverTCP
    };

    /// <summary>
    /// Empty constructor is required for the backend to be loaded by the backend factory
    /// </summary>
    public SMBBackend()
    {
        _DnsName = null!;
        _connectionParameters = null!;
        _timeouts = null!;
    }

    /// <summary>
    /// Actual constructor for the backend that accepts the url and options
    /// </summary>
    /// <param name="url">URL in Duplicati Uri format</param>
    /// <param name="options">options to be used in the backend</param>
    public SMBBackend(string url, Dictionary<string, string?> options)
    {
        if (string.IsNullOrEmpty(url))
            throw new ArgumentNullException(nameof(url));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var uri = new Utility.Uri(url);
        uri.RequireHost();
        _DnsName = uri.Host ?? "";

        var input = uri.Path.TrimEnd('/');
        var slashIndex = input.IndexOf('/');  // Find first slash to separate server and share if present.

        var auth = AuthOptionsHelper.Parse(options, uri);
        var authDomain = options.GetValueOrDefault(AUTH_DOMAIN_OPTION);
        var transport = options.GetValueOrDefault(TRANSPORT_OPTION);

        int? readBufferSize = null, writeBufferSize = null;

        var readBufferSizeConfig = options.GetValueOrDefault(READ_BUFFER_SIZE_OPTION);
        if (!string.IsNullOrWhiteSpace(readBufferSizeConfig))
            readBufferSize = int.TryParse(readBufferSizeConfig, out int value) ? value : null;

        var writeBufferSizeConfig = options.GetValueOrDefault(WRITE_BUFFER_SIZE_OPTION);
        if (!string.IsNullOrWhiteSpace(writeBufferSizeConfig))
            writeBufferSize = int.TryParse(writeBufferSizeConfig, out int value) ? value : null;

        // Normalize to 10KB minimum buffers size
        readBufferSize = readBufferSize < 1024 * 10 ? null : readBufferSize;
        writeBufferSize = writeBufferSize < 1024 * 10 ? null : writeBufferSize;

        var transportType = _transportMap.TryGetValue(
            string.IsNullOrEmpty(transport) ? DEFAULT_TRANSPORT : transport.ToLower(),
            out SMBTransportType type)
            ? type
            : throw new UserInformationException($"Transport must be one of: {string.Join(", ", _transportMap.Keys)}", "CIFSConfig");

        _timeouts = TimeoutOptionsHelper.Parse(options);
        _connectionParameters = new SMBConnectionParameters(
            _DnsName,
            transportType,
            slashIndex >= 0 ? input[..slashIndex] : input,
            slashIndex >= 0 ? input[(slashIndex + 1)..] : "",
            authDomain,
            auth.Username,
            auth.Password,
            readBufferSize,
            writeBufferSize
        );
    }

    /// <summary>
    /// Implementation of interface property to return supported command parameters
    /// </summary>
    public IList<ICommandLineArgument> SupportedCommands =>
        [
            .. AuthOptionsHelper.GetOptions(),
            new CommandLineArgument(AUTH_DOMAIN_OPTION, CommandLineArgument.ArgumentType.String, Strings.SMBBackend.DescriptionAuthDomainShort, Strings.SMBBackend.DescriptionAuthDomainLong),
            new CommandLineArgument(TRANSPORT_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.Options.TransportShort, Strings.Options.TransportLong, DEFAULT_TRANSPORT, null, _transportMap.Keys.ToArray()),
            new CommandLineArgument(READ_BUFFER_SIZE_OPTION, CommandLineArgument.ArgumentType.String, Strings.Options.DescriptionReadBufferSizeShort, Strings.Options.DescriptionReadBufferSizeLong),
            new CommandLineArgument(WRITE_BUFFER_SIZE_OPTION, CommandLineArgument.ArgumentType.String, Strings.Options.DescriptionWriteBufferSizeShort, Strings.Options.DescriptionWriteBufferSizeLong),
            .. TimeoutOptionsHelper.GetOptions()
        ];

    /// <summary>
    /// Implementation of interface method for listing remote folder contents
    /// </summary>
    /// <returns>List of IFileEntry with directory listing result</returns>
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var con = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var v in await con.ListAsync(_connectionParameters.Path, cancellationToken).ConfigureAwait(false))
            yield return v;
    }

    /// <summary>
    /// Upload files to remote location
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="localname">Filename to read from</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution</exception>
    public async Task PutAsync(string remotename, string localname, CancellationToken cancellationToken)
    {
        await using var fs = File.Open(localname,
            FileMode.Open, FileAccess.Read, FileShare.Read);
        await PutAsync(remotename, fs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Upload files to remote location
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="input">Stream to read from</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution</exception>
    public async Task PutAsync(string remotename, Stream input, CancellationToken cancellationToken)
    {
        var con = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await con.PutAsync(remotename, input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Download files from remote
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="localname">Local filename to write to</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution or FileMissingException</exception>
    public async Task GetAsync(string remotename, string localname, CancellationToken cancellationToken)
    {
        await using var fs = File.Open(localname,
            FileMode.Create, FileAccess.Write,
            FileShare.None);
        await GetAsync(remotename, fs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Download files from remote
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="output">Destination stream to write to</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution or FileMissingException</exception>
    public async Task GetAsync(string remotename, Stream output, CancellationToken cancellationToken)
    {
        var con = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await con.GetAsync(remotename, output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete remote file if it exists, if not, throws FileMissingException
    /// </summary>
    /// <param name="remotename">filename to be deleted on the remote</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <returns></returns>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution or business logic errors</exception>
    public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
    {
        var con = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await con.DeleteAsync(remotename, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Implementation of interface function to return hosnames used by the backend
    /// </summary>
    /// <param name="cancellationToken">CancellationToken, in this call not used.</param>
    /// <returns></returns>
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new[] { _DnsName ?? string.Empty });

    /// <summary>
    /// Tests backend connectivity by verifying the configured path exists
    /// </summary>
    /// <param name="cancellationToken">The cancellation token (not used)</param>
    /// <exception cref="FolderMissingException">Thrown when configured path does not exist</exception>
    public Task TestAsync(CancellationToken cancellationToken)
        => this.TestReadWritePermissionsAsync(cancellationToken);

    /// <summary>
    /// Creates the configured remote folder path if it doesn't exist
    /// </summary>
    /// <param name="cancellationToken">CancellationToken that will be combined with internal timeout token</param>
    /// <returns>Task representing the asynchronous operation</returns>
    /// <exception cref="Exception">Thrown when folder creation fails</exception>
    public async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
        var pathParts = _connectionParameters.Path?
            .Split(PATH_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);

        if (pathParts == null || pathParts.Length == 0)
            return;

        var con = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        await con.CreateFolderAsync(_connectionParameters.Path ?? "", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Gets or creates a shared SMB connection
    /// </summary>
    /// <returns>An SMB connection that can be used for file operations</returns>
    private async Task<SMBShareConnection> GetConnectionAsync(CancellationToken cancellationToken)
        => _sharedConnection ??= await SMBShareConnection.CreateAsync(_connectionParameters, _timeouts, cancellationToken).ConfigureAwait(false);



    /// <summary>
    /// Implementation of Dispose pattern enforced by interface
    /// </summary>
    public void Dispose()
    {
        try
        {
            _sharedConnection?.Dispose();
        }
        catch (Exception ex)
        {
            // Log the exception but don't rethrow since we're in Dispose
            System.Diagnostics.Debug.WriteLine($"Error disposing CIFS connection: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IFileEntry> ListAsync(string? path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sourcePath = _connectionParameters.Path;
        if (!string.IsNullOrWhiteSpace(sourcePath))
            sourcePath = Util.AppendDirSeparator(sourcePath, "/");

        var con = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var v in await con.ListAsync(sourcePath + BackendSourceFileEntry.NormalizePathTo(path, '/'), cancellationToken).ConfigureAwait(false))
            if (v.Name != "." && v.Name != "..")
                yield return v;
    }

    /// <inheritdoc/>
    public Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult<IFileEntry?>(null);
}