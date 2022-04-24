#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Localization.Short;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
namespace Duplicati.Library.Backend
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class Jottacloud : IBackend, IStreamingBackend
    {
        private const string AUTHID_OPTION = "authid";
        private const string API_ROOT = "https://api.jottacloud.com";
        private const string JFS_ROOT = "https://jfs.jottacloud.com/jfs";
        private const string JFS_BUILTIN_DEVICE = "Jotta"; // The built-in device used for the built-in Sync and Archive mount points.
        private static readonly string JFS_DEFAULT_BUILTIN_MOUNT_POINT = "Archive"; // When using the built-in device we pick this mount point as our default.
        private static readonly string JFS_DEFAULT_CUSTOM_MOUNT_POINT = "Duplicati"; // When custom device is specified then we pick this mount point as our default.
        private static readonly string[] JFS_BUILTIN_MOUNT_POINTS = { "Archive", "Sync" }; // Name of built-in mount points that we can use.
        private static readonly string[] JFS_BUILTIN_ILLEGAL_MOUNT_POINTS = { "Trash", "Links", "Latest", "Shared" }; // Name of built-in mount points that we can not use. These are treated as mount points in the API, but they are for used for special functionality and we cannot upload files to them!
        private const string JFS_DEVICE_OPTION = "jottacloud-device";
        private const string JFS_MOUNT_POINT_OPTION = "jottacloud-mountpoint";
        private const string JFS_THREADS = "jottacloud-threads";
        private const string JFS_CHUNKSIZE = "jottacloud-chunksize";
        private const string JFS_DATE_FORMAT = "yyyy'-'MM'-'dd'-T'HH':'mm':'ssK";
        private readonly string m_device;
        private readonly bool m_deviceBuiltin;
        private readonly string m_mountPoint;
        private readonly string m_path;
        private readonly string m_fullPath;
        private readonly string m_jfsUserUrl;
        private readonly string m_jfsDeviceUrl;
        private readonly string m_jfsUrl;
        private readonly byte[] m_copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];

        private static readonly string JFS_DEFAULT_CHUNKSIZE = "5mb";
        private static readonly string JFS_DEFAULT_THREADS = "4";
        private readonly int m_threads;
        private readonly long m_chunksize;

        private readonly JottacloudAuthHelper m_oauth;

        /// <summary>
        /// The default maximum number of concurrent connections allowed by a ServicePoint object is 2.
        /// It should be increased to allow multiple download threads.
        /// https://stackoverflow.com/a/44637423/1105812
        /// </summary>
        static Jottacloud()
        {
            System.Net.ServicePointManager.DefaultConnectionLimit = 1000;
        }

        public Jottacloud()
        {
        }

        public Jottacloud(string url, Dictionary<string, string> options)
        {
            // Duplicati back-end url for Jottacloud is in format "jottacloud://folder/subfolder", we transform them to
            // the Jottacloud REST API (JFS) url format "https://jfs.jottacloud.com/jfs/[username]/[device]/[mountpoint]/[folder]/[subfolder]".

            // Find out what JFS device to use.
            if (options.ContainsKey(JFS_DEVICE_OPTION))
            {
                // Custom device specified.
                m_device = options[JFS_DEVICE_OPTION];
                if (string.Equals(m_device, JFS_BUILTIN_DEVICE, StringComparison.OrdinalIgnoreCase))
                {
                    m_deviceBuiltin = true; // Device is configured, but value set to the built-in device!
                    m_device = JFS_BUILTIN_DEVICE; // Ensure correct casing (doesn't seem to matter, but in theory it could).
                }
                else
                {
                    m_deviceBuiltin = false;
                }
            }
            else
            {
                // Use default: The built-in device.
                m_device = JFS_BUILTIN_DEVICE;
                m_deviceBuiltin = true;
            }

            // Find out what JFS mount point to use on the device.
            if (options.ContainsKey(JFS_MOUNT_POINT_OPTION))
            {
                // Custom mount point specified.
                m_mountPoint = options[JFS_MOUNT_POINT_OPTION];

                // If we are using the built-in device make sure we have picked a mount point that we can use.
                if (m_deviceBuiltin)
                {
                    // Check that it is not set to one of the special built-in mount points that we definitely cannot make use of.
                    if (Array.FindIndex(JFS_BUILTIN_ILLEGAL_MOUNT_POINTS, x => x.Equals(m_mountPoint, StringComparison.OrdinalIgnoreCase)) != -1)
                        throw new UserInformationException(Strings.Jottacloud.IllegalMountPoint, "JottaIllegalMountPoint");
                    // Check if it is one of the legal built-in mount points.
                    // What to do if it is not is open for discussion: The JFS API supports creation of custom mount points not only
                    // for custom (backup) devices, but also for the built-in device. But this will not be visible via the official
                    // web interface, so you are kind of working in the dark and need to use the REST API to delete it etc. Therefore
                    // we do not allow this for now, although in future maybe we could consider it, as a "hidden" location?
                    var i = Array.FindIndex(JFS_BUILTIN_MOUNT_POINTS, x => x.Equals(m_mountPoint, StringComparison.OrdinalIgnoreCase));
                    if (i != -1)
                        m_mountPoint = JFS_BUILTIN_MOUNT_POINTS[i]; // Ensure correct casing (doesn't seem to matter, but in theory it could).
                    else
                        throw new UserInformationException(Strings.Jottacloud.IllegalMountPoint, "JottaIllegalMountPoint"); // User defined mount points on built-in device currently not allowed.
                }
            }
            else
            {
                if (m_deviceBuiltin)
                    m_mountPoint = JFS_DEFAULT_BUILTIN_MOUNT_POINT; // Set a suitable built-in mount point for the built-in device.
                else
                    m_mountPoint = JFS_DEFAULT_CUSTOM_MOUNT_POINT; // Set a suitable default mount point for custom (backup) devices.
            }

            string authid = null;
            if (options.ContainsKey(AUTHID_OPTION))
                authid = options[AUTHID_OPTION];
            m_oauth = new JottacloudAuthHelper(authid);

            // Build URL
            var u = new Utility.Uri(url);
            m_path = u.HostAndPath; // Host and path of "jottacloud://folder/subfolder" is "folder/subfolder", so the actual folder path within the mount point.
            if (string.IsNullOrEmpty(m_path)) // Require a folder. Actually it is possible to store files directly on the root level of the mount point, but that does not seem to be a good option.
                throw new UserInformationException(Strings.Jottacloud.NoPathError, "JottaNoPath");
            m_path = Util.AppendDirSeparator(m_path, "/");

            m_fullPath = m_device + "/" + m_mountPoint + "/" + m_path;
            m_jfsUserUrl = JFS_ROOT + "/" + m_oauth.Username;
            m_jfsDeviceUrl = m_jfsUserUrl + "/" + m_device;
            m_jfsUrl = m_jfsUserUrl + "/" + m_fullPath;

            m_threads = int.Parse(options.ContainsKey(JFS_THREADS) ? options[JFS_THREADS] : JFS_DEFAULT_THREADS);

            if (!options.TryGetValue(JFS_CHUNKSIZE, out var tmp))
            {
                tmp = JFS_DEFAULT_CHUNKSIZE;
            }

            var chunksize = Utility.Sizeparser.ParseSize(tmp, "mb");

            // Chunk size is bound by BinaryReader.ReadBytes(length) where length is an int.

            if (chunksize > int.MaxValue || chunksize < 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(chunksize), string.Format("The chunk size cannot be less than {0}, nor larger than {1}", Utility.Utility.FormatSizeString(1024), Utility.Utility.FormatSizeString(int.MaxValue)));
            }

            m_chunksize = chunksize;
        }

#region IBackend Members

        public string DisplayName
        {
            get { return Strings.Jottacloud.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "jottacloud"; }
        }

        public IEnumerable<IFileEntry> List()
        {
            var doc = new System.Xml.XmlDocument();
            try
            {
                // Send request and load XML response.
                var req = CreateJfsRequest(System.Net.WebRequestMethods.Http.Get, "", "");
                var areq = new Utility.AsyncHttpRequest(req);
                using (var rs = areq.GetResponseStream())
                    doc.Load(rs);
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FolderMissingException(wex);
                throw;
            }
            // Handle XML response. Since we in the constructor demand a folder below the mount point we know the root
            // element must be a "folder", else it could also have been a "mountPoint" (which has a very similar structure).
            // We must check for "deleted" attribute, because files/folders which has it is deleted (attribute contains the timestamp of deletion)
            // so we treat them as non-existent here.
            var xRoot = doc.DocumentElement;
            if (xRoot.Attributes["deleted"] != null)
            {
                throw new FolderMissingException();
            }
            foreach (System.Xml.XmlNode xFolder in xRoot.SelectNodes("folders/folder[not(@deleted)]"))
            {
                // Subfolders are only listed with name. We can get a timestamp by sending a request for each folder, but that is probably not necessary?
                FileEntry fe = new FileEntry(xFolder.Attributes["name"].Value);
                fe.IsFolder = true;
                yield return fe;
            }
            foreach (System.Xml.XmlNode xFile in xRoot.SelectNodes("files/file[not(@deleted)]"))
            {
                var fe = ToFileEntry(xFile);
                if (fe != null)
                {
                    yield return fe;
                }
            }
        }

        public static IFileEntry ToFileEntry(System.Xml.XmlNode xFile)
        {
            string name = xFile.Attributes["name"].Value;
            // Normal files have an "currentRevision", which represent the most recent successfully upload
            // (could also checked that currentRevision/state is "COMPLETED", but should not be necessary).
            // There might also be a newer "latestRevision" coming from an incomplete or corrupt upload,
            // but we ignore that here and use the information about the last valid version.
            System.Xml.XmlNode xRevision = xFile.SelectSingleNode("currentRevision");
            if (xRevision != null)
            {
                System.Xml.XmlNode xState = xRevision.SelectSingleNode("state");
                if (xState != null && xState.InnerText == "COMPLETED") // Think "currentRevision" always is a complete version, but just to be on the safe side..
                {
                    System.Xml.XmlNode xSize = xRevision.SelectSingleNode("size");
                    long size;
                    if (xSize == null || !long.TryParse(xSize.InnerText, out size))
                        size = -1;
                    DateTime lastModified;
                    System.Xml.XmlNode xModified = xRevision.SelectSingleNode("modified"); // There is created, modified and updated time stamps, but not last accessed.
                    if (xModified == null || !DateTime.TryParseExact(xModified.InnerText, JFS_DATE_FORMAT, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out lastModified))
                        lastModified = new DateTime();
                    FileEntry fe = new FileEntry(name, size, lastModified, lastModified);
                    return fe;
                }
            }
            return null;
        }

        /// <summary>
        /// Retrieves info for a single file (used to determine file size for chunking)
        /// </summary>
        /// <param name="remotename"></param>
        /// <returns></returns>
        public IFileEntry Info(string remotename)
        {
            var doc = new System.Xml.XmlDocument();
            try
            {
                // Send request and load XML response.
                var req = CreateJfsRequest(System.Net.WebRequestMethods.Http.Get, remotename, "");
                var areq = new Utility.AsyncHttpRequest(req);
                using (var rs = areq.GetResponseStream())
                    doc.Load(rs);
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                throw;
            }
            // Handle XML response. Since we in the constructor demand a folder below the mount point we know the root
            // element must be a "folder", else it could also have been a "mountPoint" (which has a very similar structure).
            // We must check for "deleted" attribute, because files/folders which has it is deleted (attribute contains the timestamp of deletion)
            // so we treat them as non-existent here.
            var xFile = doc.DocumentElement;
            if (xFile.Attributes["deleted"] != null)
            {
                throw new FileMissingException(string.Format("{0}: {1}", LC.L("The requested file does not exist"), remotename));
            }

            return ToFileEntry(xFile);
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                await PutAsync(remotename, fs, cancelToken);
        }

        public void Get(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
                Get(remotename, fs);
        }

        public void Delete(string remotename)
        {
            System.Net.HttpWebRequest req = CreateJfsRequest(System.Net.WebRequestMethods.Http.Post, remotename, "rm=true"); // rm=true means permanent delete, dl=true would be move to trash.
            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
            { }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get 
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(AUTHID_OPTION, CommandLineArgument.ArgumentType.Password, Strings.Jottacloud.AuthidShort, Strings.Jottacloud.AuthidLong(OAuthHelper.OAUTH_LOGIN_URL("jottacloud"))),
                    new CommandLineArgument(JFS_DEVICE_OPTION, CommandLineArgument.ArgumentType.String, Strings.Jottacloud.DescriptionDeviceShort, Strings.Jottacloud.DescriptionDeviceLong(JFS_MOUNT_POINT_OPTION)),
                    new CommandLineArgument(JFS_MOUNT_POINT_OPTION, CommandLineArgument.ArgumentType.String, Strings.Jottacloud.DescriptionMountPointShort, Strings.Jottacloud.DescriptionMountPointLong(JFS_DEVICE_OPTION)),
                    new CommandLineArgument(JFS_THREADS, CommandLineArgument.ArgumentType.Integer, Strings.Jottacloud.ThreadsShort, Strings.Jottacloud.ThreadsLong, JFS_DEFAULT_THREADS),
                    new CommandLineArgument(JFS_CHUNKSIZE, CommandLineArgument.ArgumentType.Size, Strings.Jottacloud.ChunksizeShort, Strings.Jottacloud.ChunksizeLong, JFS_DEFAULT_CHUNKSIZE),
                });
            }
        }

        public string Description
        {
            get { return Strings.Jottacloud.Description; }
        }

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            // When using custom (backup) device we must create the device first (if not already exists).
            if (!m_deviceBuiltin)
            {
                System.Net.HttpWebRequest req = CreateJfsRequest(System.Net.WebRequestMethods.Http.Post, m_jfsDeviceUrl, "type=WORKSTATION"); // Hard-coding device type. Must be one of "WORKSTATION", "LAPTOP", "IMAC", "MACBOOK", "IPAD", "ANDROID", "IPHONE" or "WINDOWS_PHONE".
                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                { }
            }
            // Create the folder path, and if using custom mount point it will be created as well in the same operation.
            {
                System.Net.HttpWebRequest req = CreateJfsRequest(System.Net.WebRequestMethods.Http.Post, "", "mkDir=true");
                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                { }
            }
        }

#endregion

#region IDisposable Members

        public void Dispose()
        {
        }

#endregion

        private System.Net.HttpWebRequest CreateRequest(string method, string url, string queryparams)
        {
            return m_oauth.CreateRequest(url + (string.IsNullOrEmpty(queryparams) || queryparams.Trim().Length == 0 ? "" : "?" + queryparams), method);
        }

        private System.Net.HttpWebRequest CreateJfsRequest(string method, string remotename, string queryparams)
        {
            var url = m_jfsUrl + Library.Utility.Uri.UrlEncode(remotename).Replace("+", "%20");
            return CreateRequest(method, url, queryparams);
        }

        public string[] DNSName
        {
            get { return new string[] { new Uri(JFS_ROOT).Host, new Uri(API_ROOT).Host }; }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            if (m_threads > 1)
            {
                ParallelGet(remotename, stream);
                return;
            }
            // Downloading from Jottacloud: Will only succeed if the file has a completed revision,
            // and if there are multiple versions of the file we will only get the latest completed version,
            // ignoring any incomplete or corrupt versions.
            var req = CreateJfsRequest(System.Net.WebRequestMethods.Http.Get, remotename, "mode=bin");
            var areq = new Utility.AsyncHttpRequest(req);
            using (var s = areq.GetResponseStream())
                Utility.Utility.CopyStream(s, stream, true, m_copybuffer);
        }

        /// <summary>
        /// Fetches the file in chunks (parallelized)
        /// </summary>
        public void ParallelGet(string remotename, System.IO.Stream stream)
        {
            var size = Info(remotename).Size;

            var chunks = new Queue<Tuple<long, long>>(); // Tuple => Position (from), Position (to)

            long position = 0;

            while (position < size)
            {
                var length = Math.Min(m_chunksize, size - position);
                chunks.Enqueue(new Tuple<long, long>(position, position + length));
                position += length;
            }

            var tasks = new Queue<Task<byte[]>>();

            while (tasks.Count > 0 || chunks.Count > 0)
            {
                while (chunks.Count > 0 && tasks.Count < m_threads)
                {
                    var item = chunks.Dequeue();
                    tasks.Enqueue(Task.Run(() =>
                    {
                        var req = CreateJfsRequest(System.Net.WebRequestMethods.Http.Get, remotename, "mode=bin");
                        req.AddRange(item.Item1, item.Item2 - 1);
                        var areq = new Utility.AsyncHttpRequest(req);
                        using (var s = areq.GetResponseStream())
                        using (var reader = new System.IO.BinaryReader(s))
                        {
                            var length = item.Item2 - item.Item1;
                            return reader.ReadBytes((int)length);
                        }
                    }));
                }
                var buffer = tasks.Dequeue().Result;
                stream.Write(buffer, 0, buffer.Length);
            }
        }

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            // Jottacloud requires size and MD5 as part of the request. If the stream is not
            // seek-able we have to spool it into a temporary file while calculating MD5,
            // and then we also get the size. Even if the stream is seek-able it may be throttled,
            // and therefore we try to avoid using the throttled stream for calculating the MD5
            // underlying stream from the "m_basestream" field, with fall-back to a temporary file.
            Utility.TempFile tmpFile = null;
            var baseStream = stream;
            while (baseStream is Utility.OverrideableStream)
                baseStream = typeof(Utility.OverrideableStream).GetField("m_basestream", System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(baseStream) as System.IO.Stream;
            if (baseStream == null)
                throw new Exception(string.Format("Unable to unwrap stream from: {0}", stream.GetType()));
            string md5Hash;
            if (baseStream.CanSeek)
            {
                var originalPosition = baseStream.Position;
                using (var md5 = System.Security.Cryptography.MD5.Create())
                    md5Hash = Utility.Utility.ByteArrayAsHexString(md5.ComputeHash(baseStream));
                baseStream.Position = originalPosition;
            }
            else
            {
                // No seeking possible, use a temp file
                tmpFile = new Utility.TempFile();
                using (var os = System.IO.File.OpenWrite(tmpFile))
                using (var md5 = new Utility.MD5CalculatingStream(baseStream))
                {
                    await Utility.Utility.CopyStreamAsync(md5, os, true, cancelToken, m_copybuffer).ConfigureAwait(false);
                    md5Hash = md5.GetFinalHashString();
                }
                stream = System.IO.File.OpenRead(tmpFile);
            }
            try
            {
                // Send initial allocate request, performing deduplication and preparing resume upload.
                var fileSize = stream.Length;
                var data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new JottacloudAllocateRequest()
                {
                    Path = "/jfs/" + m_fullPath + remotename,
                    Bytes = fileSize,
                    MD5 = md5Hash
                }));
                var allocateResponse = await m_oauth.GetJSONDataAsync<JottacloudAllocateResponse>(
                    API_ROOT + "/files/v1/allocate", cancelToken,
                    req =>
                    {
                        req.Method = WebRequestMethods.Http.Post;
                        req.ContentType = "application/json; charset=UTF-8";
                        req.ContentLength = data.Length;

                    },
                     async (areq, areqCancelToken) =>
                     {
                         using (var rs = areq.GetRequestStream())
                             await rs.WriteAsync(data, 0, data.Length, areqCancelToken).ConfigureAwait(false);
                     }
                ).ConfigureAwait(false);
                // Check result.
                // If state is COMPLETED then no content needs to be uploaded, because destination
                // file matches - possibly after updating timestamp of existing file, updating content
                // of existing file with deduplication, or creating new file with deduplication.
                // Else, we must upload data from an indicated position to make destination file
                // match size or checksum.
                if (allocateResponse.State != "COMPLETED")
                {
                    var uploadResponse = await m_oauth.GetJSONDataAsync<JottacloudAllocateUploadResponse>(
                        allocateResponse.UploadUrl, cancelToken,
                        req =>
                        {
                            req.Method = WebRequestMethods.Http.Post;
                            req.ContentType = "application/octet-stream";
                            if (allocateResponse.ResumePos > 0)
                            {
                                req.ContentLength = fileSize - allocateResponse.ResumePos;
                                req.AddRange(allocateResponse.ResumePos);
                            }
                            else
                            {
                                req.ContentLength = fileSize;
                            }
                        },
                        async (areq, areqCancelToken) =>
                        {
                            using (var rs = areq.GetRequestStream())
                            {
                                if (allocateResponse.ResumePos > 0)
                                {
                                    // Resumed upload, discard initial bytes already matching.
                                    if (baseStream.CanSeek)
                                    {
                                        baseStream.Seek(allocateResponse.ResumePos, System.IO.SeekOrigin.Current);
                                    }
                                    else
                                    {
                                        int read, chunk;
                                        long offset = allocateResponse.ResumePos;
                                        do
                                        {
                                            chunk = offset > m_copybuffer.Length ? m_copybuffer.Length : (int)offset;
                                            read = await baseStream.ReadAsync(m_copybuffer, 0, chunk, areqCancelToken).ConfigureAwait(false);
                                            offset -= read;
                                        } while (offset > 0 && read > 0);
                                        if (offset > 0) // Hit end of the stream before reaching resume position!
                                            throw new WebException(Strings.Jottacloud.FileUploadError, WebExceptionStatus.ProtocolError);
                                    }
                                }
                                // Copy remaining byte to resume upload.
                                await Utility.Utility.CopyStreamAsync(stream, rs, true, areqCancelToken, m_copybuffer).ConfigureAwait(false);
                            }
                        }
                    ).ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (tmpFile != null)
                        tmpFile.Dispose();
                }
                catch { }
            }
        }
        private class JottacloudAllocateRequest
        {
            [JsonProperty("path")]
            public string Path { get; set; }
            [JsonProperty("bytes")]
            public long Bytes { get; set; }
            [JsonProperty("md5")]
            public string MD5 { get; set; }
            // It can also contain "created" and "modified" timestamps
            // (formatted as strings), but since we are working with a
            // stream here we do not know the local file's timestamps,
            // and by omitting them the service will automatically set
            // the current time.
        }

        private class JottacloudAllocateResponse
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("pame")]
            public string Path { get; set; }
            [JsonProperty("state")]
            public string State { get; set; }
            [JsonProperty("bytes")]
            public long Bytes { get; set; }
            [JsonProperty("resume_pos")]
            public long ResumePos { get; set; }
            [JsonProperty("upload_id")]
            public string UploadId { get; set; }
            [JsonProperty("upload_url")]
            public string UploadUrl { get; set; }
        }

        private class JottacloudAllocateUploadResponse
        {
            [JsonProperty("content_id")]
            public string ContentId { get; set; }
            [JsonProperty("path")]
            public string Path { get; set; }
            [JsonProperty("modified")]
            public long Modified { get; set; }
            [JsonProperty("bytes")]
            public long Bytes { get; set; }
            [JsonProperty("md5")]
            public string MD5 { get; set; }
        }

    }
}
