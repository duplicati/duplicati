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

using System;
using Duplicati.Library.Interface;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.CIFS;
using Duplicati.Library.Backend.CIFS.Model;
using SMBLibrary;
using Duplicati.Library.SourceProvider;
using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend;

/// <summary>
/// Native CIFS/SMB Backend implementation
/// </summary>
public class CIFSBackend : IStreamingBackend, IFolderEnabledBackend
{
    /// <summary>
    /// Implementation of interface property for the backend key
    /// </summary>
    public string ProtocolKey => "cifs";

    /// <summary>
    /// Implementation of interface property for the backend display name
    /// </summary>
    public string DisplayName => Strings.CIFSBackend.DisplayName;

    /// <summary>
    /// Implementation of interface property for the backend description
    /// </summary>
    public string Description => Strings.CIFSBackend.Description;

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
    private SMBConnectionParameters _connectionParameters;

    /// <summary>
    /// Shared connection between all methods to avoid re-authentication
    /// </summary>
    private SMBShareConnection _sharedConnection;

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
    /// Username for authentication
    /// </summary>
    private const string AUTH_USERNAME_OPTION = "auth-username";

    /// <summary>
    /// Password for authentication
    /// </summary>
    private const string AUTH_PASSWORD_OPTION = "auth-password";

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
    public CIFSBackend()
    {
    }

    /// <summary>
    /// Actual constructor for the backend that accepts the url and options
    /// </summary>
    /// <param name="url">URL in Duplicati Uri format</param>
    /// <param name="options">options to be used in the backend</param>
    public CIFSBackend(string url, Dictionary<string, string> options)
    {
        if (string.IsNullOrEmpty(url))
            throw new ArgumentNullException(nameof(url));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var uri = new Utility.Uri(url);
        uri.RequireHost();
        _DnsName = uri.Host;

        var input = uri.Path.TrimEnd('/');
        var slashIndex = input.IndexOf('/');  // Find first slash to separate server and share if present.

        options.TryGetValue(AUTH_USERNAME_OPTION, out string authUsername);
        options.TryGetValue(AUTH_PASSWORD_OPTION, out string authPassword);
        options.TryGetValue(AUTH_DOMAIN_OPTION, out string authDomain);
        options.TryGetValue(TRANSPORT_OPTION, out string transport);

        int? readBufferSize = null, writeBufferSize = null;

        options.TryGetValue(READ_BUFFER_SIZE_OPTION, out string readBufferSizeConfig);
        if (!string.IsNullOrWhiteSpace(readBufferSizeConfig)) readBufferSize = Int32.TryParse(readBufferSizeConfig, out int value) ? value : null;

        options.TryGetValue(WRITE_BUFFER_SIZE_OPTION, out string writeBufferSizeConfig);
        if (!string.IsNullOrWhiteSpace(writeBufferSizeConfig)) writeBufferSize = Int32.TryParse(readBufferSizeConfig, out int value) ? value : null;

        // Normalize to 10KB minimum buffers size
        readBufferSize = readBufferSize < 1024 * 10 ? null : readBufferSize;
        writeBufferSize = writeBufferSize < 1024 * 10 ? null : writeBufferSize;

        SMBTransportType transportType = _transportMap.TryGetValue(
            string.IsNullOrEmpty(transport) ? DEFAULT_TRANSPORT : transport.ToLower(),
            out SMBTransportType type)
            ? type
            : throw new UserInformationException($"Transport must be one of: {string.Join(", ", _transportMap.Keys)}", "CIFSConfig");

        _connectionParameters = new SMBConnectionParameters(
            uri.Host,
            transportType,
            slashIndex >= 0 ? input[..slashIndex] : input,
            slashIndex >= 0 ? input[(slashIndex + 1)..] : "",
            authDomain,
            authUsername,
            authPassword,
            readBufferSize,
            writeBufferSize
        );
    }

    /// <summary>
    /// Implementation of interface property to return supported command parameters
    /// </summary>
    public IList<ICommandLineArgument> SupportedCommands =>
        new List<ICommandLineArgument>([
            new CommandLineArgument(AUTH_PASSWORD_OPTION, CommandLineArgument.ArgumentType.Password, Strings.CIFSBackend.DescriptionAuthPasswordShort, Strings.CIFSBackend.DescriptionAuthPasswordLong),
            new CommandLineArgument(AUTH_USERNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.CIFSBackend.DescriptionAuthUsernameShort, Strings.CIFSBackend.DescriptionAuthUsernameLong),
            new CommandLineArgument(AUTH_DOMAIN_OPTION, CommandLineArgument.ArgumentType.String, Strings.CIFSBackend.DescriptionAuthDomainShort, Strings.CIFSBackend.DescriptionAuthDomainLong),
            new CommandLineArgument(TRANSPORT_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.Options.TransportShort, Strings.Options.TransportLong, DEFAULT_TRANSPORT, null, _transportMap.Keys.ToArray()),
            new CommandLineArgument(READ_BUFFER_SIZE_OPTION, CommandLineArgument.ArgumentType.String, Strings.Options.DescriptionReadBufferSizeShort, Strings.Options.DescriptionReadBufferSizeLong),
            new CommandLineArgument(WRITE_BUFFER_SIZE_OPTION, CommandLineArgument.ArgumentType.String, Strings.Options.DescriptionWriteBufferSizeShort, Strings.Options.DescriptionWriteBufferSizeLong)
        ]);

    /// <summary>
    /// Implementation of interface method for listing remote folder contents
    /// </summary>
    /// <returns>List of IFileEntry with directory listing result</returns>
    public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var v in await GetConnection().ListAsync(_connectionParameters.Path, cancellationToken).ConfigureAwait(false))
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
        await GetConnection().PutAsync(remotename, input, cancellationToken).ConfigureAwait(false);
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
        await GetConnection().GetAsync(remotename, output, cancellationToken).ConfigureAwait(false);
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
        await GetConnection().DeleteAsync(remotename, cancellationToken).ConfigureAwait(false);
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
    public async Task TestAsync(CancellationToken cancellationToken)
    {
        // This will throw an exception if the folder is missing
        await GetConnection().ListAsync(_connectionParameters.Path, cancellationToken).ConfigureAwait(false);
    }

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

        await GetConnection().CreateFolderAsync(_connectionParameters.Path, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Gets or creates a shared SMB connection
    /// </summary>
    /// <returns>An SMB connection that can be used for file operations</returns>
    private SMBShareConnection GetConnection() => _sharedConnection ??= new SMBShareConnection(_connectionParameters);



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
    public async IAsyncEnumerable<IFileEntry> ListAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sourcePath = _connectionParameters.Path;
        if (!string.IsNullOrWhiteSpace(sourcePath))
            sourcePath = Util.AppendDirSeparator(sourcePath, "/");

        foreach (var v in await GetConnection().ListAsync(sourcePath + BackendSourceFileEntry.NormalizePathTo(path, '/'), cancellationToken).ConfigureAwait(false))
            if (v.Name != "." && v.Name != "..")
                yield return v;
    }

    /// <inheritdoc/>
    public Task<IFileEntry> GetEntryAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult<IFileEntry>(null);
}