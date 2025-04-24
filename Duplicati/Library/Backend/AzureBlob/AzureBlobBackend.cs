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

using Azure;
using Azure.Storage.Blobs.Models;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;

namespace Duplicati.Library.Backend.AzureBlob
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class AzureBlobBackend : IStreamingBackend
    {
        /// <summary>
        /// The access to Azure blob storage
        /// </summary>
        private readonly AzureBlobWrapper _azureBlob;

        /// <summary>
        /// The option to specify the Azure storage account name
        /// </summary>
        private const string AZURE_ACCOUNT_NAME_OPTION = "azure-account-name";
        /// <summary>
        /// The option to specify the Azure access key
        /// </summary>
        private const string AZURE_ACCESS_KEY_OPTION = "azure-access-key";
        /// <summary>
        /// The option to specify the Azure access SAS token
        /// </summary>
        private const string AZURE_ACCESS_SAS_TOKEN_OPTION = "azure-access-sas-token";
        /// <summary>
        /// The option to specify the archive classes
        /// </summary>
        private const string AZURE_ARCHIVE_CLASSES_OPTION = "azure-archive-classes";
        /// <summary>
        /// The option to specify the Azure access tier
        /// </summary>
        private const string AZURE_ACCESS_TIER_OPTION = "azure-access-tier";

        /// <summary>
        /// The default storage classes that are considered archive classes
        /// </summary>
        private static readonly IReadOnlySet<AccessTier> DEFAULT_ARCHIVE_CLASSES = new HashSet<AccessTier>([
            AccessTier.Cool, AccessTier.Cold, AccessTier.Archive
        ]);

        /// <summary>
        /// List of access tiers
        /// </summary>
        private static readonly IEnumerable<AccessTier> ACCESS_TIERS =
            typeof(AccessTier).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(x => x.PropertyType == typeof(AccessTier))
                .Select(x => x.GetValue(null) as AccessTier?)
                .WhereNotNull()
                .ToArray();

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public AzureBlobBackend()
        {
            _azureBlob = null!;
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public AzureBlobBackend(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);
            uri.RequireHost();

            var containerName = uri.Host.ToLowerInvariant();

            var auth = AuthOptionsHelper.ParseWithAlias(options, uri, AZURE_ACCOUNT_NAME_OPTION, AZURE_ACCESS_KEY_OPTION);
            var timeouts = TimeoutOptionsHelper.Parse(options);

            var sasToken = options.GetValueOrDefault(AZURE_ACCESS_SAS_TOKEN_OPTION);
            if (!auth.HasUsername)
                throw new UserInformationException(Strings.AzureBlobBackend.NoStorageAccountName, "AzureNoAccountName");

            if (!auth.HasPassword && string.IsNullOrWhiteSpace(sasToken))
                throw new UserInformationException(Strings.AzureBlobBackend.NoAccessKeyOrSasToken, "AzureNoAccessKeyOrSasToken");

            var archiveClasses = ParseStorageClasses(options.GetValueOrDefault(AZURE_ARCHIVE_CLASSES_OPTION));
            var accessTierValue = options.GetValueOrDefault(AZURE_ACCESS_TIER_OPTION);
            var accessTier = string.IsNullOrWhiteSpace(accessTierValue)
                ? null
                // Warning: The cast here is required to avoid implicit casting null to AccessTier
                : (AccessTier?)new AccessTier(accessTierValue);
            _azureBlob = new AzureBlobWrapper(auth.Username!, auth.Password, sasToken, containerName, accessTier, archiveClasses, timeouts);
        }

        /// <summary>
        /// Parses the storage classes from the string
        /// </summary>
        /// <param name="storageClass">The storage class string</param>
        /// <returns>The storage classes</returns>
        private static IReadOnlySet<AccessTier> ParseStorageClasses(string? storageClass)
        {
            if (string.IsNullOrWhiteSpace(storageClass))
                return DEFAULT_ARCHIVE_CLASSES;

            return new HashSet<AccessTier>(storageClass.Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => new AccessTier(x)));
        }

        public string DisplayName => Strings.AzureBlobBackend.DisplayName;

        public string ProtocolKey => "azure";

        public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancelToken)
            => _azureBlob.ListContainerEntriesAsync(cancelToken);

        public async Task PutAsync(string remotename, string localname, CancellationToken cancelToken)
        {
            await using var fs = File.Open(localname,
                FileMode.Open, FileAccess.Read, FileShare.Read);
            await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public Task PutAsync(string remotename, Stream input, CancellationToken cancelToken)
        {
            return WrapWithExceptionHandler(_azureBlob.AddFileStream(remotename, input, cancelToken));
        }

        public async Task GetAsync(string remotename, string localname, CancellationToken cancellationToken)
        {
            await using var fs = File.Open(localname,
                FileMode.Create, FileAccess.Write,
                FileShare.None);
            await GetAsync(remotename, fs, cancellationToken).ConfigureAwait(false);
        }

        public Task GetAsync(string remotename, Stream output, CancellationToken cancellationToken)
        {
            return WrapWithExceptionHandler(_azureBlob.GetFileStreamAsync(remotename, output, cancellationToken));
        }

        public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
        {
            return WrapWithExceptionHandler(_azureBlob.DeleteObjectAsync(remotename, cancellationToken));
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return [
                    new CommandLineArgument(AZURE_ACCOUNT_NAME_OPTION,
                        CommandLineArgument.ArgumentType.String,
                        Strings.AzureBlobBackend.StorageAccountNameDescriptionShort,
                        Strings.AzureBlobBackend.StorageAccountNameDescriptionLong,
                        null,
                        [AuthOptionsHelper.AuthUsernameOption]),
                    new CommandLineArgument(AZURE_ACCESS_KEY_OPTION,
                        CommandLineArgument.ArgumentType.Password,
                        Strings.AzureBlobBackend.AccessKeyDescriptionShort,
                        Strings.AzureBlobBackend.AccessKeyDescriptionLong,
                        null,
                        [AuthOptionsHelper.AuthPasswordOption]),
                    new CommandLineArgument(AZURE_ACCESS_SAS_TOKEN_OPTION,
                        CommandLineArgument.ArgumentType.Password,
                        Strings.AzureBlobBackend.SasTokenDescriptionShort,
                        Strings.AzureBlobBackend.SasTokenDescriptionLong),
                    new CommandLineArgument(AZURE_ARCHIVE_CLASSES_OPTION,
                        CommandLineArgument.ArgumentType.Flags,
                        Strings.AzureBlobBackend.ArchiveClassesDescriptionShort,
                        Strings.AzureBlobBackend.ArchiveClassesDescriptionLong,
                        string.Join(",", DEFAULT_ARCHIVE_CLASSES.Select(x => x.ToString())),
                        null,
                        ACCESS_TIERS.Select(x => x.ToString()).ToArray()),
                    new CommandLineArgument(AZURE_ACCESS_TIER_OPTION,
                        CommandLineArgument.ArgumentType.String,
                        Strings.AzureBlobBackend.AccessTierDescriptionShort,
                        Strings.AzureBlobBackend.AccessTierDescriptionLong,
                        "",
                        null,
                        ACCESS_TIERS.Select(x => x.ToString()).ToArray()),
                    .. TimeoutOptionsHelper.GetOptions()
                ];
            }
        }

        public string Description => Strings.AzureBlobBackend.DescriptionV2;

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(_azureBlob.DnsNames);

        public Task TestAsync(CancellationToken cancellationToken)
            => this.TestListAsync(cancellationToken);

        public Task CreateFolderAsync(CancellationToken cancellationToken)
        {
            return WrapWithExceptionHandler(_azureBlob.AddContainerAsync(cancellationToken));
        }

        /// <summary>
        /// Wraps the task with exception handling
        /// </summary>
        private async Task WrapWithExceptionHandler(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (RequestFailedException e)
                when (e.Status == 404
                      || e.ErrorCode == BlobErrorCode.BlobNotFound
                      || e.ErrorCode == BlobErrorCode.ResourceNotFound)
            {
                throw new FileMissingException(e.Message, e);
            }
            catch (RequestFailedException e)
                when (e.ErrorCode == BlobErrorCode.ContainerNotFound
                      || e.ErrorCode == BlobErrorCode.ContainerBeingDeleted
                      || e.ErrorCode == BlobErrorCode.ContainerDisabled)
            {
                throw new FolderMissingException(e.Message, e);
            }
        }

        public void Dispose()
        {

        }
    }
}
