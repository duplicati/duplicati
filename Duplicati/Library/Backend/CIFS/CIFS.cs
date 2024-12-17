// Copyright (C) 2024, The Duplicati Team
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
using Duplicati.Library.Utility;
using SMBLibrary;

namespace Duplicati.Library.Backend;

/// <summary>
/// Native CIFS/SMB Backend implementation
/// </summary>
public class CIFSBackend : IStreamingBackend
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

    private SMBConnectionParameters _connectionParameters;

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
        var uri = new Utility.Uri(url);
        uri.RequireHost();
        _DnsName = uri.Host;
        
        var input = uri.Path.TrimEnd('/');
        var slashIndex = input.IndexOf('/');  // Find first slash to separate server and share if present.
        
        options.TryGetValue(AUTH_USERNAME_OPTION, out string authUsername);
        options.TryGetValue(AUTH_PASSWORD_OPTION, out string authPassword);
        options.TryGetValue(AUTH_DOMAIN_OPTION, out string authDomain);
        options.TryGetValue(TRANSPORT_OPTION, out string transport);
        
        SMBTransportType transportType = _transportMap.TryGetValue(
            string.IsNullOrEmpty(transport) ? DEFAULT_TRANSPORT : transport.ToLower(), 
            out SMBTransportType type)
            ? type
            : throw new UserInformationException($"Transport must be one of: {string.Join(", ", _transportMap.Keys)}","CIFSConfig");
        
        _connectionParameters = new SMBConnectionParameters(
            uri.Host,
            transportType,
            slashIndex >= 0 ? input[..slashIndex] : input,
            slashIndex >= 0 ? input[(slashIndex + 1)..] : "",
            authDomain,
            authUsername,
            authPassword
        );
    }

    /// <summary>
    /// Implementation of interface property to return supported command parameters
    /// </summary>
    public IList<ICommandLineArgument> SupportedCommands
    {
        get
        {
            return new List<ICommandLineArgument>(new ICommandLineArgument[]
            {
                new CommandLineArgument(AUTH_PASSWORD_OPTION, CommandLineArgument.ArgumentType.Password, Strings.CIFSBackend.DescriptionAuthPasswordShort, Strings.CIFSBackend.DescriptionAuthPasswordLong),
                new CommandLineArgument(AUTH_USERNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.CIFSBackend.DescriptionAuthUsernameShort, Strings.CIFSBackend.DescriptionAuthUsernameLong),
                new CommandLineArgument(AUTH_DOMAIN_OPTION, CommandLineArgument.ArgumentType.String, Strings.CIFSBackend.DescriptionAuthDomainShort, Strings.CIFSBackend.DescriptionAuthDomainLong),
                new CommandLineArgument(TRANSPORT_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.Options.TransportShort, Strings.Options.TransportLong, DEFAULT_TRANSPORT, null, _transportMap.Keys.ToArray()),
                });
        }
    }
    
    /// <summary>
    /// Implementation of interface method for listing remote folder contents
    /// </summary>
    /// <returns>List of IFileEntry with directory listing result</returns>
    private async Task<IEnumerable<IFileEntry>> ListAsync(CancellationToken cancellationToken)
    {
        await using var shareConnection = new SMBShareConnection(_connectionParameters);
        return await shareConnection.ListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Wrapper method of legacy non async call to list files in the remote folder
    /// </summary>
    /// <returns></returns>
    public IEnumerable<IFileEntry> List() => ListAsync(CancellationToken.None).Await();

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
        await using var shareConnection = new SMBShareConnection(_connectionParameters);
        await shareConnection.PutAsync(remotename, input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Download files from remote
    /// </summary>
    /// <param name="remotename">Filename at remote location</param>
    /// <param name="output">Destination stream to write to</param>
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
        await using var shareConnection = new SMBShareConnection(_connectionParameters);
        await shareConnection.GetAsync(remotename, output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete remote file if it exists, if now, throws FileMissingException
    /// </summary>
    /// <param name="remotename">filename to be deleted on the remote</param>
    /// <param name="cancellationToken">CancellationToken that is combined with internal timeout token</param>
    /// <returns></returns>
    /// <exception cref="FileMissingException">FileMissingException when file is not found</exception>
    /// <exception cref="Exception">Exceptions arising from either code execution or business logic when return code from pcloud indicates an error.</exception>
    public async Task DeleteAsync(string remotename, CancellationToken cancellationToken)
    {
        await using var shareConnection = new SMBShareConnection(_connectionParameters);
        await shareConnection.DeleteAsync(remotename, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Implementation of interface function to return hosnames used by the backend
    /// </summary>
    /// <param name="cancellationToken">CancellationToken, in this call not used.</param>
    /// <returns></returns>
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancellationToken) => Task.FromResult(new[] { _DnsName });

    /// <summary>
    /// Tests backend connectivity by verifying the configured path exists
    /// </summary>
    /// <param name="cancellationToken">The cancellation token (not used)</param>
    /// <exception cref="FolderMissingException">Thrown when configured path does not exist</exception>
    public async Task TestAsync(CancellationToken cancellationToken)
    {
        await using var shareConnection = new SMBShareConnection(_connectionParameters);
        // This will throw an exception if the folder is missing
        await shareConnection.ListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Create remote folder
    /// </summary>
    /// <param name="cancellationToken">CancellationToken that will be combined with internal timeout token</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionParameters.Path) || _connectionParameters.Path.Split(PATH_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Length == 0)
            return;
        
        await using var shareConnection = new SMBShareConnection(_connectionParameters);
        await shareConnection.CreateFolderAsync(_connectionParameters.Path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Implementation of Dispose pattern enforced by interface
    /// in this case, we don't need to dispose anything
    /// </summary>
    public void Dispose()
    {
    }
}