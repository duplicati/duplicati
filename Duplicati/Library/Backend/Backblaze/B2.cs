using System.Net.Http.Headers;
using System.Net.Http;
//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Backblaze
{
    public class B2 : IBackend
    {
        private const string B2_ID_OPTION = "b2-accountid";
        private const string B2_KEY_OPTION = "b2-applicationkey";
		private const string B2_PAGESIZE_OPTION = "b2-page-size";
        private const string B2_DOWNLOAD_URL_OPTION = "b2-download-url";

		private const string B2_CREATE_BUCKET_TYPE_OPTION = "b2-create-bucket-type";
        private const string DEFAULT_BUCKET_TYPE = "allPrivate";

        private const int DEFAULT_PAGE_SIZE = 500;

        private readonly string m_bucketname;
        private readonly string m_prefix;
        private readonly string m_urlencodedprefix;
        private readonly string m_bucketType;
        private readonly int m_pagesize;
        private readonly string m_downloadUrl;
        private readonly B2AuthHelper m_helper;
        private UploadUrlResponse m_uploadUrl;

        private Dictionary<string, List<FileEntity>> m_filecache;

        private BucketEntity m_bucket;

        public B2()
        {
        }

        public B2(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_bucketname = uri.Host;
            m_prefix = Util.AppendDirSeparator("/" + uri.Path, "/");

            // For B2 we do not use a leading slash
            while(m_prefix.StartsWith("/", StringComparison.Ordinal))
                m_prefix = m_prefix.Substring(1);

            m_urlencodedprefix = string.Join("/", m_prefix.Split(new [] { '/' }).Select(x => Library.Utility.Uri.UrlPathEncode(x)));

            m_bucketType = DEFAULT_BUCKET_TYPE;
            if (options.ContainsKey(B2_CREATE_BUCKET_TYPE_OPTION))
                m_bucketType = options[B2_CREATE_BUCKET_TYPE_OPTION];

            string accountId = null;
            string accountKey = null;

            if (options.ContainsKey("auth-username"))
                accountId = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                accountKey = options["auth-password"];

            if (options.ContainsKey(B2_ID_OPTION))
                accountId = options[B2_ID_OPTION];
            if (options.ContainsKey(B2_KEY_OPTION))
                accountKey = options[B2_KEY_OPTION];
            if (!string.IsNullOrEmpty(uri.Username))
                accountId = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                accountKey = uri.Password;

            if (string.IsNullOrEmpty(accountId))
                throw new UserInformationException(Strings.B2.NoB2UserIDError, "B2MissingUserID");
            if (string.IsNullOrEmpty(accountKey))
                throw new UserInformationException(Strings.B2.NoB2KeyError, "B2MissingKey");
            
            m_helper = new B2AuthHelper(accountId, accountKey);

			m_pagesize = DEFAULT_PAGE_SIZE;
            if (options.ContainsKey(B2_PAGESIZE_OPTION))
            {
                int.TryParse(options[B2_PAGESIZE_OPTION], out m_pagesize);

                if (m_pagesize <= 0)
                    throw new UserInformationException(Strings.B2.InvalidPageSizeError(B2_PAGESIZE_OPTION, options[B2_PAGESIZE_OPTION]), "B2InvalidPageSize");
            }

            m_downloadUrl = null;
            if (options.ContainsKey(B2_DOWNLOAD_URL_OPTION))
            {
                m_downloadUrl = options[B2_DOWNLOAD_URL_OPTION];
            }
		}

        private async Task<BucketEntity> GetBucketAsync(CancellationToken cancelToken)
        {
            if (m_bucket == null)
            {
                var buckets = await m_helper.PostAndGetJSONDataAsync<ListBucketsResponse>(
                    string.Format("{0}/b2api/v1/b2_list_buckets", await m_helper.GetAPIUrlAsync(cancelToken)),
                    new ListBucketsRequest() {
                        AccountID = await m_helper.GetAccountIDAsync(cancelToken)
                    },
                    null,
                    cancelToken
                );

                if (buckets != null && buckets.Buckets != null)
                    m_bucket = buckets.Buckets.FirstOrDefault(x => string.Equals(x.BucketName, m_bucketname, StringComparison.OrdinalIgnoreCase));

                if (m_bucket == null)
                    throw new FolderMissingException();
            }

            return m_bucket;
        }

        private async Task<UploadUrlResponse> GetUploadUrlDataAsync(CancellationToken cancelToken)
        {
            if (m_uploadUrl == null)
                m_uploadUrl = await m_helper.PostAndGetJSONDataAsync<UploadUrlResponse>(
                    string.Format("{0}/b2api/v1/b2_get_upload_url", await m_helper.GetAPIUrlAsync(cancelToken)),
                    new UploadUrlRequest() { BucketID = (await GetBucketAsync(cancelToken)).BucketID },
                    null,
                    cancelToken
                );

            return m_uploadUrl;
        }

        private async Task<string> GetFileIDAsync(string filename, CancellationToken cancelToken)
        {
            if (m_filecache != null && m_filecache.ContainsKey(filename))
                return m_filecache[filename].OrderByDescending(x => x.UploadTimestamp).First().FileID;

            await ListAsync(cancelToken);

            if (m_filecache.ContainsKey(filename))
                return m_filecache[filename].OrderByDescending(x => x.UploadTimestamp).First().FileID;

            throw new FileMissingException();
        }

        private async Task<string> GetDownloadUrlAsync(CancellationToken cancelToken) {
            if (string.IsNullOrEmpty(m_downloadUrl)) {
                return await m_helper.GetDownloadUrlAsync(cancelToken);
            } else {
                return m_downloadUrl;
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(B2_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.B2.B2accountidDescriptionShort, Strings.B2.B2accountidDescriptionLong, null, new string[] {"auth-password"}, null),
                    new CommandLineArgument(B2_KEY_OPTION, CommandLineArgument.ArgumentType.Password, Strings.B2.B2applicationkeyDescriptionShort, Strings.B2.B2applicationkeyDescriptionLong, null, new string[] {"auth-username"}, null),
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.B2.AuthPasswordDescriptionShort, Strings.B2.AuthPasswordDescriptionLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.B2.AuthUsernameDescriptionShort, Strings.B2.AuthUsernameDescriptionLong),
                    new CommandLineArgument(B2_CREATE_BUCKET_TYPE_OPTION, CommandLineArgument.ArgumentType.String, Strings.B2.B2createbuckettypeDescriptionShort, Strings.B2.B2createbuckettypeDescriptionLong, DEFAULT_BUCKET_TYPE),
                    new CommandLineArgument(B2_PAGESIZE_OPTION, CommandLineArgument.ArgumentType.Integer, Strings.B2.B2pagesizeDescriptionShort, Strings.B2.B2pagesizeDescriptionLong, DEFAULT_PAGE_SIZE.ToString()),
                    new CommandLineArgument(B2_DOWNLOAD_URL_OPTION, CommandLineArgument.ArgumentType.String, Strings.B2.B2downloadurlDescriptionShort, Strings.B2.B2downloadurlDescriptionLong),
				});

            }
        }

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            TempFile tmp = null;

            // A bit dirty, but we need the underlying stream to compute the hash without any interference
            var measure = stream;
            while (measure is OverrideableStream)
                measure = typeof(OverrideableStream).GetField("m_basestream", System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(measure) as System.IO.Stream;

            if (measure == null)
                throw new Exception(string.Format("Unable to unwrap stream from: {0}", stream.GetType()));

            string sha1;
            if (measure.CanSeek)
            {
                var p = measure.Position;

                // Compute the hash
                using(var hashalg = HashAlgorithm.Create("sha1"))
                    sha1 = Library.Utility.Utility.ByteArrayAsHexString(hashalg.ComputeHash(measure));

                measure.Position = p;
            }
            else
            {
                // No seeking possible, use a temp file
                tmp = new TempFile();
                using(var sr = System.IO.File.OpenWrite(tmp))
                using(var hc = new HashCalculatingStream(measure, "sha1"))
                {
                    await Utility.Utility.CopyStreamAsync(hc, sr, cancelToken).ConfigureAwait(false);
                    sha1 = hc.GetFinalHashString();
                }

                stream = System.IO.File.OpenRead(tmp);
            }

            if (m_filecache == null)
                await ListAsync(cancelToken);

            try
            {
                var updata = await GetUploadUrlDataAsync(cancelToken);
                var fileinfo = await m_helper.GetJSONDataAsync<UploadFileResponse>(
                    updata.UploadUrl,                    
                    req =>
                    {
                        req.Method = new HttpMethod("POST");
                        req.Headers.Add("Authorization", updata.AuthorizationToken);
                        req.Headers.Add("X-Bz-Content-Sha1", sha1);
                        req.Headers.Add("X-Bz-File-Name", m_urlencodedprefix + Utility.Uri.UrlPathEncode(remotename));
                        var body = new StreamContent(stream);
                        body.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                        body.Headers.ContentLength = stream.Length;
                        req.Content = body;
                    },
                    cancelToken
                ).ConfigureAwait(false);

                // Delete old versions
                if (m_filecache.ContainsKey(remotename))
                    await DeleteAsync(remotename, cancelToken).ConfigureAwait(false);;

                m_filecache[remotename] = new List<FileEntity>();                
                m_filecache[remotename].Add(new FileEntity() {
                    FileID = fileinfo.FileID,
                    FileName = fileinfo.FileName,
                    Action = "upload",
                    Size = fileinfo.ContentLength,
                    UploadTimestamp = (long)(DateTime.UtcNow - Utility.Utility.EPOCH).TotalMilliseconds
                });
            }
            catch(Exception ex)
            {
                m_filecache = null;

                var code = (int)B2AuthHelper.GetExceptionStatusCode(ex);
                if (code >= 500 && code <= 599)
                    m_uploadUrl = null;
                
                throw;
            }
            finally
            {
                tmp?.Dispose();
            }
        }

        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            HttpRequestMessage req;
            if (m_filecache == null || !m_filecache.ContainsKey(remotename))
                await ListAsync(cancelToken);

            if (m_filecache != null && m_filecache.ContainsKey(remotename))
                req = await m_helper.CreateRequestAsync(string.Format("{0}/b2api/v1/b2_download_file_by_id?fileId={1}", await GetDownloadUrlAsync(cancelToken), Library.Utility.Uri.UrlEncode(await GetFileIDAsync(remotename, cancelToken))), null , cancelToken);
            else
                req = await m_helper.CreateRequestAsync(string.Format("{0}/{1}{2}", await GetDownloadUrlAsync(cancelToken), m_urlencodedprefix, Library.Utility.Uri.UrlPathEncode(remotename)), null, cancelToken);

            try
            {
                using(var resp =  await m_helper.SendAsync(req, cancelToken))
                    await Duplicati.Library.Utility.Utility.CopyStreamAsync(await resp.Content.ReadAsStreamAsync(), stream, cancelToken);
            }
            catch (Exception ex)
            {
                if (B2AuthHelper.GetExceptionStatusCode(ex) == HttpStatusCode.NotFound)
                    throw new FileMissingException();

                await B2AuthHelper.AttemptParseAndThrowExceptionAsync(ex);

                throw;
            }
        }

        public async Task<IList<IFileEntry>> ListAsync(CancellationToken cancelToken)
        {
            m_filecache = null;
            var cache = new Dictionary<string, List<FileEntity>>();
            string nextFileID = null;
            string nextFileName = null;
            do
            {
                var resp = await m_helper.PostAndGetJSONDataAsync<ListFilesResponse>(
                    string.Format("{0}/b2api/v1/b2_list_file_versions", await m_helper.GetAPIUrlAsync(cancelToken)),
                    new ListFilesRequest() {
                        BucketID = (await GetBucketAsync(cancelToken)).BucketID,
                        MaxFileCount = m_pagesize,
                        Prefix = m_prefix,
                        StartFileID = nextFileID,
                        StartFileName = nextFileName
                    },
                    null,
                    cancelToken
                );

                nextFileID = resp.NextFileID;
                nextFileName = resp.NextFileName;

                if (resp.Files == null || resp.Files.Length == 0)
                    break;

                foreach(var f in resp.Files)
                {
                    if (!f.FileName.StartsWith(m_prefix, StringComparison.Ordinal))
                        continue;

                    var name = f.FileName.Substring(m_prefix.Length);
                    if (name.Contains("/"))
                        continue;


                    List<FileEntity> lst;
                    cache.TryGetValue(name, out lst);
                    if (lst == null)
                        cache[name] = lst = new List<FileEntity>(1);
                    lst.Add(f);
                }

            } while(nextFileID != null);

            m_filecache = cache;

            return 
                (from x in m_filecache
                    let newest = x.Value.OrderByDescending(y => y.UploadTimestamp).First()
                    let ts = Utility.Utility.EPOCH.AddMilliseconds(newest.UploadTimestamp)
                    select (IFileEntry)new FileEntry(x.Key, newest.Size, ts, ts)
                ).ToList();
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                if (m_filecache == null || !m_filecache.ContainsKey(remotename))
                    await ListAsync(cancelToken);

                if (!m_filecache.ContainsKey(remotename))
                    throw new FileMissingException();
                
                foreach(var n in m_filecache[remotename].OrderBy(x => x.UploadTimestamp))
                    await m_helper.PostAndGetJSONDataAsync<DeleteResponse>(
                        string.Format("{0}/b2api/v1/b2_delete_file_version", await m_helper.GetAPIUrlAsync(cancelToken)),
                        new DeleteRequest() {
                            FileName = m_prefix + remotename,
                            FileID = n.FileID
                        },
                        null,
                        cancelToken
                    );

                m_filecache[remotename].Clear();
            }
            catch
            {
                m_filecache = null;
                throw;
            }
        }

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            m_bucket = await m_helper.PostAndGetJSONDataAsync<BucketEntity>(
                string.Format("{0}/b2api/v1/b2_create_bucket", await m_helper.GetAPIUrlAsync(cancelToken)),
                new BucketEntity() {
                    AccountID = await m_helper.GetAccountIDAsync(cancelToken),
                    BucketName = m_bucketname,
                    BucketType = m_bucketType
                },
                null,
                cancelToken
            );
        }

        public string DisplayName
        {
            get { return Strings.B2.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "b2"; }
        }

        public string Description
        {
            get { return Strings.B2.Description; }
        }

        public string[] DNSName
        {
            get { return new string[] { new System.Uri(B2AuthHelper.AUTH_URL).Host, m_helper?.APIDnsName, m_helper?.DownloadDnsName} ; }
        }

        public bool SupportsStreaming => true;


        public void Dispose()
        {
        }

        private class DeleteRequest
        {
            [JsonProperty("fileName")]
            public string FileName { get; set; }
            [JsonProperty("fileId")]
            public string FileID { get; set; }
        }

        private class DeleteResponse : DeleteRequest
        {
        }

        private class UploadUrlRequest : BucketIDEntity
        {
        }

        private class UploadUrlResponse : BucketIDEntity
        {
            [JsonProperty("uploadUrl")]
            public string UploadUrl { get; set; }
            [JsonProperty("authorizationToken")]
            public string AuthorizationToken { get; set; }
        }

        private class AccountIDEntity
        {
            [JsonProperty("accountId")]
            public string AccountID { get; set; }
        }

        private class BucketIDEntity
        {
            [JsonProperty("bucketId")]
            public string BucketID { get; set; }
        }

        private class BucketEntity : AccountIDEntity
        {
            [JsonProperty("bucketId", NullValueHandling = NullValueHandling.Ignore)]
            public string BucketID { get; set; }
            [JsonProperty("bucketName")]
            public string BucketName { get; set; }
            [JsonProperty("bucketType")]
            public string BucketType { get; set; }
        }

        private class ListBucketsRequest : AccountIDEntity
        {
        }

        private class ListBucketsResponse
        {
            [JsonProperty("buckets")]
            public BucketEntity[] Buckets { get; set; }
        }

        private class ListFilesRequest : BucketIDEntity
        {
            [JsonProperty("startFileName", NullValueHandling = NullValueHandling.Ignore)]
            public string StartFileName { get; set; }
            [JsonProperty("startFileId", NullValueHandling = NullValueHandling.Ignore)]
            public string StartFileID { get; set; }
            [JsonProperty("maxFileCount")]
            public long MaxFileCount { get; set; }
            [JsonProperty("prefix")]
            public string Prefix { get; set; }
        }

        private class ListFilesResponse
        {
            [JsonProperty("nextFileName")]
            public string NextFileName { get; set; }
            [JsonProperty("nextFileId")]
            public string NextFileID { get; set; }
            [JsonProperty("files")]
            public FileEntity[] Files { get; set; }
        }

        private class FileEntity
        {
            [JsonProperty("fileId")]
            public string FileID { get; set; }
            [JsonProperty("fileName")]
            public string FileName { get; set; }
            [JsonProperty("action")]
            public string Action { get; set; }
            [JsonProperty("size")]
            public long Size { get; set; }
            [JsonProperty("uploadTimestamp")]
            public long UploadTimestamp { get; set; }

        }

        private class UploadFileResponse : AccountIDEntity
        {
            [JsonProperty("bucketId")]
            public string BucketID { get; set; }
            [JsonProperty("fileId")]
            public string FileID { get; set; }
            [JsonProperty("fileName")]
            public string FileName { get; set; }
            [JsonProperty("contentLength")]
            public long ContentLength { get; set; }
            [JsonProperty("contentSha1")]
            public string ContentSha1 { get; set; }
            [JsonProperty("contentType")]
            public string ContentType { get; set; }
        }

    }
}

