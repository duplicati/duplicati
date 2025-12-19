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


using Amazon.S3;
using Amazon.S3.Model;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Helper class that fixes long list support and injects location headers, includes using directives etc.
    /// </summary>
    public class S3AwsClient : IS3Client
    {
        /// <summary>
        /// The maximum number of items to list in a single request
        /// </summary>
        private const int ITEM_LIST_LIMIT = 1000;

        /// <summary>
        /// The prefix for extended options
        /// </summary>
        private const string EXT_OPTION_PREFIX = "s3-ext-";

        /// <summary>
        /// The location constraint for the bucket
        /// </summary>
        private readonly string? m_locationConstraint;
        /// <summary>
        /// The storage class for the bucket
        /// </summary>
        private readonly string? m_storageClass;
        /// <summary>
        /// The S3 client
        /// </summary>
        private readonly AmazonS3Client m_client;
        /// <summary>
        /// The option to specify if chunk encoding should be used
        /// </summary>
        private readonly bool m_useChunkEncoding;
        /// <summary>
        /// The option to specify if payload signing should be disabled
        /// </summary>
        private readonly bool m_disablePayloadSigning;

        /// <summary>
        /// The DNS host of the S3 server
        /// </summary>
        private readonly string? m_dnsHost;
        /// <summary>
        /// The option to specify if the V2 list API should be used
        /// </summary>
        private readonly bool m_useV2ListApi;
        /// <summary>
        /// The timeouts to use
        /// </summary>
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;

        /// <summary>
        /// The archive classes that are considered archive classes
        /// </summary>
        private readonly IReadOnlySet<S3StorageClass> m_archiveClasses;

        /// <summary>
        /// The lock mode to use for object locking
        /// </summary>
        private readonly string m_lockMode;

        /// <summary>
        /// The option to specify the archive classes
        /// </summary>
        public const string S3_ARCHIVE_CLASSES_OPTION = "s3-archive-classes";

        /// <summary>
        /// The default storage classes that are considered archive classes
        /// </summary>
        public static readonly IReadOnlySet<S3StorageClass> DEFAULT_ARCHIVE_CLASSES = new HashSet<S3StorageClass>([
            S3StorageClass.DeepArchive, S3StorageClass.Glacier, S3StorageClass.GlacierInstantRetrieval, S3StorageClass.Snow
        ]);

        public S3AwsClient(string awsID, string awsKey, string? locationConstraint, string servername,
            string? storageClass, bool useSSL, bool disableChunkEncoding, bool disablePayloadSigning, TimeoutOptionsHelper.Timeouts timeouts, Dictionary<string, string?> options, string lockMode)
        {
            var cfg = GetDefaultAmazonS3Config();
            cfg.UseHttp = !useSSL;
            cfg.ServiceURL = (useSSL ? "https://" : "http://") + servername;

            CommandLineArgumentMapper.ApplyArguments(cfg, options, EXT_OPTION_PREFIX);

            m_client = new AmazonS3Client(awsID, awsKey, cfg);

            m_timeouts = timeouts;
            m_useV2ListApi = string.Equals(options.GetValueOrDefault("list-api-version", "v1"), "v2", StringComparison.OrdinalIgnoreCase);
            m_locationConstraint = locationConstraint;
            m_storageClass = storageClass;
            m_dnsHost = string.IsNullOrWhiteSpace(cfg.ServiceURL) ? null : new System.Uri(cfg.ServiceURL).Host;
            m_useChunkEncoding = !disableChunkEncoding;
            m_disablePayloadSigning = disablePayloadSigning;
            m_archiveClasses = ParseStorageClasses(options.GetValueOrDefault(S3_ARCHIVE_CLASSES_OPTION));
            m_lockMode = lockMode;
        }

        /// <summary>
        /// Parses the storage classes from the string
        /// </summary>
        /// <param name="storageClass">The storage class string</param>
        /// <returns>The storage classes</returns>
        private static IReadOnlySet<S3StorageClass> ParseStorageClasses(string? storageClass)
        {
            if (string.IsNullOrWhiteSpace(storageClass))
                return DEFAULT_ARCHIVE_CLASSES;

            return new HashSet<S3StorageClass>(storageClass.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => new S3StorageClass(x)));
        }

        /// <inheritdoc/>
        public async Task AddBucketAsync(string bucketName, CancellationToken cancelToken)
        {
            var request = new PutBucketRequest
            {
                BucketName = bucketName,
            };

            if (!string.IsNullOrEmpty(m_locationConstraint))
                request.BucketRegionName = m_locationConstraint;

            try
            {
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => m_client.PutBucketAsync(request, ct)).ConfigureAwait(false);
            }
            catch (AmazonS3Exception s3ex)
            {
                TranslateException(s3ex, bucketName);
                throw;
            }
        }

        /// <summary>
        /// Gets the default Amazon S3 configuration
        /// </summary>
        /// <returns>>The default Amazon S3 configuration</returns>
        public static AmazonS3Config GetDefaultAmazonS3Config()
        {
            return new AmazonS3Config()
            {
                BufferSize = (int)Utility.Utility.DEFAULT_BUFFER_SIZE,

                // If this is not set, accessing the property will trigger an expensive operation (~30 seconds)
                // to get the region endpoint. The use of ARNs (Amazon Resource Names) doesn't appear to be
                // critical for our usages.
                // See: https://docs.aws.amazon.com/general/latest/gr/aws-arns-and-namespaces.html
                UseArnRegion = false,
            };
        }

        /// <summary>
        /// Extended options that are not included as reported options
        /// </summary>
        private static readonly HashSet<string> EXCLUDED_EXTENDED_OPTIONS = new HashSet<string>([
            nameof(AmazonS3Config.USEast1RegionalEndpointValue)
        ]);

        /// <summary>
        /// List of properties that are slow to read the default value from
        /// </summary>
        /// <remarks>Changes in this list will likely need to be reflected in AWSSecretProvider.cs</remarks>
        private static readonly HashSet<string> SLOW_LOADING_PROPERTIES = new[] {
            nameof(AmazonS3Config.RegionEndpoint),
            nameof(AmazonS3Config.ServiceURL),
            nameof(AmazonS3Config.MaxErrorRetry),
            nameof(AmazonS3Config.DefaultConfigurationMode),
            nameof(AmazonS3Config.Timeout),
            nameof(AmazonS3Config.RetryMode),
        }.ToHashSet();

        public static IEnumerable<ICommandLineArgument> GetAwsExtendedOptions()
            => CommandLineArgumentMapper.MapArguments(GetDefaultAmazonS3Config(), prefix: EXT_OPTION_PREFIX, exclude: EXCLUDED_EXTENDED_OPTIONS, excludeDefaultValue: SLOW_LOADING_PROPERTIES)
                .Cast<CommandLineArgument>()
                .Select(x =>
                {
                    x.LongDescription = $"Extended option {x.LongDescription}";
                    return x;
                });

        public virtual async Task GetFileStreamAsync(string bucketName, string keyName, Stream target, CancellationToken cancelToken)
        {
            try
            {
                var objectGetRequest = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName
                };

                using (var objectGetResponse = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => m_client.GetObjectAsync(objectGetRequest, ct)).ConfigureAwait(false))
                using (var s = objectGetResponse.ResponseStream)
                using (var t = s.ObserveReadTimeout(m_timeouts.ReadWriteTimeout))
                    await Utility.Utility.CopyStreamAsync(t, target, cancelToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception s3Ex)
            {
                TranslateException(s3Ex, keyName);
                throw;
            }

        }

        public string? GetDnsHost()
        {
            return m_dnsHost;
        }

        public virtual async Task AddFileStreamAsync(string bucketName, string keyName, Stream source,
            CancellationToken cancelToken)
        {
            (source, var hashes, var tmp) = await Utility.Utility.CalculateThrottledStreamHash(source, ["MD5", "SHA256"], cancelToken).ConfigureAwait(false);
            using var _ = tmp;

            // Precalculate the hashes to avoid calculating them in the PutObjectAsync call where the stream could be throttled
            var md5 = Convert.ToBase64String(Utility.Utility.HexStringAsByteArray(hashes[0]));
            var sha256 = Convert.ToBase64String(Utility.Utility.HexStringAsByteArray(hashes[1]));

            using var ts = source.ObserveReadTimeout(m_timeouts.ReadWriteTimeout, false);
            var objectAddRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = keyName,
                InputStream = ts,
                UseChunkEncoding = m_useChunkEncoding,
                MD5Digest = md5,
                ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
                ChecksumSHA256 = sha256,
                DisablePayloadSigning = m_disablePayloadSigning
            };
            if (!string.IsNullOrWhiteSpace(m_storageClass))
                objectAddRequest.StorageClass = new S3StorageClass(m_storageClass);

            // Provide SigV4 payload hash explicitly (lowercase hex) iff applicable.
            // - If chunked streaming is ON, the SDK uses the streaming literal.
            // - If payload signing is disabled, the SDK uses the UNSIGNED-PAYLOAD literal.
            if (!m_useChunkEncoding && !m_disablePayloadSigning)
                objectAddRequest.Headers["x-amz-content-sha256"] = hashes[1].ToLowerInvariant();

            try
            {
                await m_client.PutObjectAsync(objectAddRequest, cancelToken);
            }
            catch (AmazonS3Exception s3Ex)
            {
                TranslateException(s3Ex, keyName);
                throw;
            }
        }

        public async Task DeleteObjectAsync(string bucketName, string keyName, CancellationToken cancellationToken)
        {
            var objectDeleteRequest = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = keyName
            };

            try
            {
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancellationToken, ct => m_client.DeleteObjectAsync(objectDeleteRequest, ct)).ConfigureAwait(false);
            }
            catch (AmazonS3Exception s3Ex)
            {
                TranslateException(s3Ex, keyName);
                throw;
            }
        }

        public async Task<DateTime?> GetObjectLockUntilAsync(string bucketName, string keyName, CancellationToken cancelToken)
        {
            var request = new GetObjectRetentionRequest
            {
                BucketName = bucketName,
                Key = keyName
            };

            try
            {
                var response = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct => m_client.GetObjectRetentionAsync(request, ct)).ConfigureAwait(false);
                return response.Retention?.RetainUntilDate?.ToUniversalTime();
            }
            catch (AmazonS3Exception s3Ex)
            {
                if ("NoSuchObjectLockConfiguration".Equals(s3Ex.ErrorCode, StringComparison.OrdinalIgnoreCase))
                    return null;

                TranslateException(s3Ex, keyName);
                throw;
            }
        }

        public async Task SetObjectLockUntilAsync(string bucketName, string keyName, DateTime lockUntilUtc, CancellationToken cancelToken)
        {
            var lockMode = typeof(ObjectLockRetentionMode)
                .GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                .FirstOrDefault(x => x.Name.Equals(m_lockMode, StringComparison.OrdinalIgnoreCase))?
                .GetValue(null) as ObjectLockRetentionMode
                    ?? new ObjectLockRetentionMode(m_lockMode);

            var request = new PutObjectRetentionRequest
            {
                BucketName = bucketName,
                Key = keyName,
                Retention = new ObjectLockRetention
                {
                    Mode = lockMode,
                    RetainUntilDate = lockUntilUtc.ToUniversalTime(),
                }
            };

            try
            {
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct =>
                    m_client.PutObjectRetentionAsync(request, ct)).ConfigureAwait(false);
            }
            catch (AmazonS3Exception s3Ex)
            {
                TranslateException(s3Ex, keyName);
                throw;
            }
        }

        /// <summary>
        /// Lists the contents of a bucket, using a plugable API call
        /// </summary>
        /// <param name="bucketName">The bucket to list</param>
        /// <param name="prefix">The prefix to list</param>
        /// <param name="recursive">If true, the list is recursive</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The list of files</returns>
        public async IAsyncEnumerable<IFileEntry> ListBucketAsync(string bucketName, string prefix, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var isTruncated = true;
            string? filename = null;
            var delimiter = recursive ? "" : "/";
            if (!string.IsNullOrWhiteSpace(prefix))
                prefix = Util.AppendDirSeparator(prefix, "/");

            //TODO: Figure out if this is the case with AWSSDK too
            //Unfortunately S3 sometimes reports duplicate values when requesting more than one page of results
            //So, track the files that have already been returned and skip any duplicates.
            var alreadyReturned = new HashSet<string>();

            //We truncate after ITEM_LIST_LIMIT elements, and then repeat
            while (isTruncated)
            {
                ListObjectsV2Response listResponse;
                try
                {
                    if (m_useV2ListApi)
                    {
                        var listRequest = new ListObjectsV2Request
                        {
                            BucketName = bucketName,
                            Prefix = prefix,
                            ContinuationToken = filename,
                            MaxKeys = ITEM_LIST_LIMIT,
                            Delimiter = delimiter
                        };
                        listResponse = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancellationToken, ct => m_client.ListObjectsV2Async(listRequest, ct)).ConfigureAwait(false);
                    }
                    else
                    {
                        // Use the V1 API
                        var listRequest = new ListObjectsRequest
                        {
                            BucketName = bucketName,
                            Prefix = prefix,
                            Marker = filename,
                            MaxKeys = ITEM_LIST_LIMIT,
                            Delimiter = delimiter
                        };
                        var listResponsev1 = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancellationToken, ct => m_client.ListObjectsAsync(listRequest, ct)).ConfigureAwait(false);

                        // Map the V1 response to the V2 response
                        listResponse = new ListObjectsV2Response
                        {
                            CommonPrefixes = listResponsev1.CommonPrefixes,
                            IsTruncated = listResponsev1.IsTruncated,
                            NextContinuationToken = listResponsev1.NextMarker,
                            S3Objects = listResponsev1.S3Objects
                        };
                    }
                }
                catch (AmazonS3Exception s3ex)
                {
                    TranslateException(s3ex, prefix ?? "");
                    throw;
                }

                isTruncated = listResponse.IsTruncated ?? false;
                filename = listResponse.NextContinuationToken;

                foreach (var obj in listResponse.CommonPrefixes ?? [])
                {
                    if (obj == prefix || !obj.StartsWith(prefix))
                        continue;

                    // Because the prefixes are returned, and not the folder objects
                    // we do not get the folder modification date
                    yield return new FileEntry(
                        obj.Substring(prefix.Length),
                        -1,
                        new DateTime(0),
                        new DateTime(0)
                    )
                    { IsFolder = true };
                }

                foreach (var obj in (listResponse.S3Objects ?? []).Where(obj => alreadyReturned.Add(obj.Key)))
                {
                    // Skip self-prefix, this discards the folder modification date :/
                    if (obj.Key == prefix || !obj.Key.StartsWith(prefix))
                        continue;

                    yield return new FileEntry(
                        obj.Key.Substring(prefix.Length),
                        obj.Size ?? -1,
                        obj.LastModified ?? default,
                        obj.LastModified ?? default
                    )
                    {
                        IsFolder = obj.Key.EndsWith("/"),
                        IsArchived = m_archiveClasses.Contains(obj.StorageClass)
                    };
                }
            }
        }

        public async Task RenameFileAsync(string bucketName, string source, string target, CancellationToken cancelToken)
        {
            var copyObjectRequest = new CopyObjectRequest
            {
                SourceBucket = bucketName,
                SourceKey = source,
                DestinationBucket = bucketName,
                DestinationKey = target
            };

            try
            {
                await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, ct => m_client.CopyObjectAsync(copyObjectRequest, ct)).ConfigureAwait(false);
                await DeleteObjectAsync(bucketName, source, cancelToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception s3Ex)
            {
                TranslateException(s3Ex, source);
                throw;
            }
        }

        private static void TranslateException(AmazonS3Exception s3Ex, string keyName)
        {
            if ("NoSuchKey".Equals(s3Ex.ErrorCode, StringComparison.OrdinalIgnoreCase))
                throw new FileMissingException(string.Format("File {0} not found", keyName), s3Ex);

            if ("NoSuchBucket".Equals(s3Ex.ErrorCode, StringComparison.OrdinalIgnoreCase))
                throw new FolderMissingException(s3Ex);

            // Fallback for non-AWS servers
            if (s3Ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new FileMissingException(string.Format("File {0} not found", keyName), s3Ex);
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (m_client != null)
                m_client.Dispose();
        }

        #endregion
    }
}