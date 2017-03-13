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
using System;
using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class Jottacloud : IBackend, IStreamingBackend
    {
        private const string JFS_ROOT = "https://jfs.jottacloud.com/jfs";
        private const string JFS_ROOT_UPLOAD = "https://up.jottacloud.com/jfs"; // Separate host for uploading files
        private const string API_VERSION = "2.4"; // Hard coded per 09. March 2017.
        private const string JFS_BUILTIN_DEVICE = "Jotta"; // The built-in device used for the built-in Sync and Archive mount points.
        private static readonly string JFS_DEFAULT_BUILTIN_MOUNT_POINT = "Archive"; // When using the built-in device we pick this mount point as our default.
        private static readonly string JFS_DEFAULT_CUSTOM_MOUNT_POINT = "Duplicati"; // When custom device is specified then we pick this mount point as our default.
        private static readonly string[] JFS_BUILTIN_MOUNT_POINTS = { "Archive", "Sync" }; // Name of built-in mount points that we can use.
        private static readonly string[] JFS_BUILTIN_ILLEGAL_MOUNT_POINTS = { "Trash", "Links", "Latest", "Shared" }; // Name of built-in mount points that we can not use. These are treated as mount points in the API, but they are for used for special functionality and we cannot upload files to them!
        private const string JFS_DEVICE_OPTION = "jottacloud-device";
        private const string JFS_MOUNT_POINT_OPTION = "jottacloud-mountpoint";
        private const string JFS_DATE_FORMAT = "yyyy'-'MM'-'dd-'T'HH':'mm':'ssK";
        private const bool ALLOW_USER_DEFINED_MOUNT_POINTS = false;
        private readonly string m_device;
        private readonly bool m_device_builtin;
        private readonly string m_mountPoint;
        private readonly string m_path;
        private readonly string m_url_device;
        private readonly string m_url;
        private readonly string m_url_upload;
        private System.Net.NetworkCredential m_userInfo;
        private readonly byte[] m_copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];

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
                    m_device_builtin = true; // Device is configured, but value set to the built-in device!
                    m_device = JFS_BUILTIN_DEVICE; // Ensure correct casing (doesn't seem to matter, but in theory it could).
                }
                else
                {
                    m_device_builtin = false;
                }
            }
            else
            {
                // Use default: The built-in device.
                m_device = JFS_BUILTIN_DEVICE;
                m_device_builtin = true;
            }

            // Find out what JFS mount point to use on the device.
            if (options.ContainsKey(JFS_MOUNT_POINT_OPTION))
            {
                // Custom mount point specified.
                m_mountPoint = options[JFS_MOUNT_POINT_OPTION];

                // If we are using the built-in device make sure we have picked a mount point that we can use.
                if (m_device_builtin)
                {
                    // Check that it is not set to one of the special built-in mount points that we definitely cannot make use of.
                    if (Array.FindIndex(JFS_BUILTIN_ILLEGAL_MOUNT_POINTS, x => x.Equals(m_mountPoint, StringComparison.OrdinalIgnoreCase)) != -1)
                        throw new UserInformationException(Strings.Jottacloud.IllegalMountPoint);
                    // Check if it is one of the legal built-in mount points.
                    // What to do if it is not is open for discussion: The JFS API supports creation of custom mount points not only
                    // for custom (backup) devices, but also for the built-in device. But this will not be visible via the official
                    // web interface, so you are kind of working in the dark and need to use the REST API to delete it etc. Therefore
                    // we do not allow this for now, although in future maybe we could consider it, as a "hidden" location?
                    var i = Array.FindIndex(JFS_BUILTIN_MOUNT_POINTS, x => x.Equals(m_mountPoint, StringComparison.OrdinalIgnoreCase));
                    if (i != -1)
                        m_mountPoint = JFS_BUILTIN_MOUNT_POINTS[i]; // Ensure correct casing (doesn't seem to matter, but in theory it could).
                    else
                        throw new UserInformationException(Strings.Jottacloud.IllegalMountPoint); // User defined mount points on built-in device currently not allowed.
                }
            }
            else
            {
                if (m_device_builtin)
                    m_mountPoint = JFS_DEFAULT_BUILTIN_MOUNT_POINT; // Set a suitable built-in mount point for the built-in device.
                else
                    m_mountPoint = JFS_DEFAULT_CUSTOM_MOUNT_POINT; // Set a suitable default mount point for custom (backup) devices.
            }

            // Build URL
            var u = new Utility.Uri(url);
            m_path = u.HostAndPath; // Host and path of "jottacloud://folder/subfolder" is "folder/subfolder", so the actual folder path within the mount point.
            if (string.IsNullOrEmpty(m_path)) // Require a folder. Actually it is possible to store files directly on the root level of the mount point, but that does not seem to be a good option.
                throw new UserInformationException(Strings.Jottacloud.NoPathError);
            if (!m_path.EndsWith("/"))
                m_path += "/";
            if (!string.IsNullOrEmpty(u.Username))
            {
                m_userInfo = new System.Net.NetworkCredential();
                m_userInfo.UserName = u.Username;
                if (!string.IsNullOrEmpty(u.Password))
                    m_userInfo.Password = u.Password;
                else if (options.ContainsKey("auth-password"))
                    m_userInfo.Password = options["auth-password"];
            }
            else
            {
                if (options.ContainsKey("auth-username"))
                {
                    m_userInfo = new System.Net.NetworkCredential();
                    m_userInfo.UserName = options["auth-username"];
                    if (options.ContainsKey("auth-password"))
                        m_userInfo.Password = options["auth-password"];
                }
            }
            if (m_userInfo == null || string.IsNullOrEmpty(m_userInfo.UserName))
                throw new UserInformationException(Strings.Jottacloud.NoUsernameError);
            if (m_userInfo == null || string.IsNullOrEmpty(m_userInfo.Password))
                throw new UserInformationException(Strings.Jottacloud.NoPasswordError);
            if (m_userInfo != null) // Bugfix, see http://connect.microsoft.com/VisualStudio/feedback/details/695227/networkcredential-default-constructor-leaves-domain-null-leading-to-null-object-reference-exceptions-in-framework-code
                m_userInfo.Domain = "";
            m_url_device = JFS_ROOT + "/" + m_userInfo.UserName + "/" + m_device;
            m_url        = m_url_device + "/" + m_mountPoint + "/" + m_path;
            m_url_upload = JFS_ROOT_UPLOAD + "/" + m_userInfo.UserName + "/" + m_device + "/" + m_mountPoint + "/" + m_path; // Different hostname, else identical to m_url.
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

        public List<IFileEntry> List()
        {
            var doc = new System.Xml.XmlDocument();
            try
            {
                // Send request and load XML response.
                var req = CreateRequest(System.Net.WebRequestMethods.Http.Get, "", "", false);
                var areq = new Utility.AsyncHttpRequest(req);
                using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
                using (var rs = areq.GetResponseStream())
                    doc.Load(rs);
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response is System.Net.HttpWebResponse && ((System.Net.HttpWebResponse)wex.Response).StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FolderMissingException(wex);
                throw;
            }
            // Handle XML response. Since we in the constructor demand a folder below the mount point we know the root
            // element must be a "folder", else it could also have been a "mountPoint" (which has a very similar structure).
            // We must check for "deleted" attribute, because files/folders which has it is deleted (attribute contains the timestamp of deletion)
            // so we treat them as non-existant here.
            List<IFileEntry> files = new List<IFileEntry>();
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
                files.Add(fe);
            }
            foreach (System.Xml.XmlNode xFile in xRoot.SelectNodes("files/file[not(@deleted)]"))
            {
                string name = xFile.Attributes["name"].Value;
                // Normal files have an "currentRevision", which represent the most recent successfully upload
                // (could also checked that currentRevision/state is "COMPLETED", but should not be necessary).
                // There might also be a newer "latestRevision" coming from an incomplete or corrupt upload,
                // but we ignore that here and use the information about the last valid version.
                System.Xml.XmlNode xRevision = xFile.SelectSingleNode("currentRevision");
                if (xRevision != null)
                {
                    System.Xml.XmlNode xNode = xRevision.SelectSingleNode("state");
                    if (xNode.InnerText == "COMPLETED") // Think "currentRevision" always is a complete version, but just to be on the safe side..
                    {
                        xNode = xRevision.SelectSingleNode("size");
                        long size;
                        if (xNode == null || !long.TryParse(xNode.InnerText, out size))
                            size = -1;
                        DateTime lastModified;
                        xNode = xRevision.SelectSingleNode("modified"); // There is created, modified and updated time stamps, but not last accessed.
                        if (xNode == null || !DateTime.TryParseExact(xNode.InnerText, JFS_DATE_FORMAT, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out lastModified))
                            lastModified = new DateTime();
                        FileEntry fe = new FileEntry(name, size, lastModified, lastModified);
                        files.Add(fe);
                    }
                }
            }
            return files;
        }

        public void Put(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                Put(remotename, fs);
        }

        public void Get(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
                Get(remotename, fs);
        }

        public void Delete(string remotename)
        {
            System.Net.HttpWebRequest req = CreateRequest(System.Net.WebRequestMethods.Http.Post, remotename, "rm=true", false); // rm=true means permanent delete, dl=true would be move to trash.
            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
            { }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get 
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.Jottacloud.DescriptionAuthPasswordShort, Strings.Jottacloud.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.Jottacloud.DescriptionAuthUsernameShort, Strings.Jottacloud.DescriptionAuthUsernameLong),
                    new CommandLineArgument(JFS_DEVICE_OPTION, CommandLineArgument.ArgumentType.String, Strings.Jottacloud.DescriptionDeviceShort, Strings.Jottacloud.DescriptionDeviceLong(JFS_MOUNT_POINT_OPTION)),
                    new CommandLineArgument(JFS_MOUNT_POINT_OPTION, CommandLineArgument.ArgumentType.String, Strings.Jottacloud.DescriptionMountPointShort, Strings.Jottacloud.DescriptionMountPointLong(JFS_DEVICE_OPTION)),
                });
            }
        }

        public string Description
        {
            get { return Strings.Jottacloud.Description; }
        }

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            // When using custom (backup) device we must create the device first (if not already exists).
            if (!m_device_builtin)
            {
                System.Net.HttpWebRequest req = CreateRequest(System.Net.WebRequestMethods.Http.Post, m_url_device, "type=WORKSTATION"); // Hard-coding device type. Must be one of "WORKSTATION", "LAPTOP", "IMAC", "MACBOOK", "IPAD", "ANDROID", "IPHONE" or "WINDOWS_PHONE".
                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                { }
            }
            // Create the folder path, and if using custom mount point it will be created as well in the same operation.
            {
                System.Net.HttpWebRequest req = CreateRequest(System.Net.WebRequestMethods.Http.Post, "", "mkDir=true", false);
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
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(url + (string.IsNullOrEmpty(queryparams) || queryparams.Trim().Length == 0 ? "" : "?" + queryparams));
            req.Method = method;
            req.Credentials = m_userInfo;
            req.PreAuthenticate = true; // We need this under Mono for some reason, and it appears some servers require this as well
            req.KeepAlive = false;
            req.UserAgent = "Duplicati Jottacloud Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            req.Headers.Add("x-jftp-version", API_VERSION);
            return req;
        }

        private System.Net.HttpWebRequest CreateRequest(string method, string remotename, string queryparams, bool upload)
        {
            var url = (upload ? m_url_upload : m_url) + Library.Utility.Uri.UrlEncode(remotename).Replace("+", "%20");
            return CreateRequest(method, url, queryparams);
        }

        #region IStreamingBackend Members

        public bool SupportsStreaming
        {
            get { return true; }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            // Downloading from Jottacloud: Will only succeed if the file has a completed revision,
            // and if there are multiple versions of the file we will only get the latest completed version,
            // ignoring any incomplete or corrupt versions.
            var req = CreateRequest(System.Net.WebRequestMethods.Http.Get, remotename, "mode=bin", false);
            var areq = new Utility.AsyncHttpRequest(req);
            using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
            using (var s = areq.GetResponseStream())
                Utility.Utility.CopyStream(s, stream, true, m_copybuffer);
        }

        public void Put(string remotename, System.IO.Stream stream)
        {
            // Some challenges with uploading to Jottacloud:
            // - Jottacloud supports use of a custom header where we can tell the server the MD5 hash of the file
            //   we are uploading, and then it will verify the content of our request against it. But the HTTP
            //   status code we get back indicates success even if there is a mismatch, so we must dig into the
            //   XML response to see if we were able to correctly upload the new content or not. Another issue 
            //   is that if the stream is not seek-able we have a challenge pre-calculating MD5 hash on it before
            //   writing it out on the HTTP request stream. And even if the stream is seek-able it may be throttled.
            //   One way to avoid using the throttled stream for calculating the MD5 is to try to get the
            //   underlying stream from the "m_basestream" field, with fall-back to a temporary file.
            // - We can instead chose to upload the data without setting the MD5 hash header. The server will
            //   calculate the MD5 on its side and return it in the response back to use. We can then compare it
            //   with the MD5 hash of the stream (using a MD5CalculatingStream), and if there is a mismatch we can
            //   request the server to remove the file again and throw an exception. But there is a requirement that
            //   we specify the file size in a custom header. And if the stream is not seek-able we are not able
            //   to use stream.Length, so we are back at square one.
            Duplicati.Library.Utility.TempFile tmpFile = null;
            var baseStream = stream;
            while (baseStream is Duplicati.Library.Utility.OverrideableStream)
                baseStream = typeof(Duplicati.Library.Utility.OverrideableStream).GetField("m_basestream", System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(baseStream) as System.IO.Stream;
            if (baseStream == null)
                throw new Exception(string.Format("Unable to unwrap stream from: {0}", stream.GetType()));
            string md5Hash;
            if (baseStream.CanSeek)
            {
                var originalPosition = baseStream.Position;
                using (var md5 = System.Security.Cryptography.MD5.Create())
                    md5Hash = Library.Utility.Utility.ByteArrayAsHexString(md5.ComputeHash(baseStream));
                baseStream.Position = originalPosition;
            }
            else
            {
                // No seeking possible, use a temp file
                tmpFile = new Duplicati.Library.Utility.TempFile();
                using (var os = System.IO.File.OpenWrite(tmpFile))
                using (var md5 = new Utility.MD5CalculatingStream(baseStream))
                {
                    Library.Utility.Utility.CopyStream(md5, os, true, m_copybuffer);
                    md5Hash = md5.GetFinalHashString();
                }
                stream = System.IO.File.OpenRead(tmpFile);
            }
            try
            {
                // Create request, with query parameter, and a few custom headers.
                // NB: If we wanted to we could send the same POST request as below but without the file contents
                // and with "cphash=[md5Hash]" as the only query parameter. Then we will get an HTTP 200 (OK) response
                // if an identical file already exists, and we can skip uploading the new file. We will get
                // HTTP 404 (Not Found) if file does not exists or it exists with a different hash, in which
                // case we must send a new request to upload the new content.
                var fileSize = stream.Length;
                var req = CreateRequest(System.Net.WebRequestMethods.Http.Post, remotename, "umode=nomultipart", true);
                req.Headers.Add("JMd5", md5Hash); // Not required, but it will make the server verify the content and mark the file as corrupt if there is a mismatch.
                req.Headers.Add("JSize", fileSize.ToString()); // Required, and used to mark file as incomplete if we upload something  be the total size of the original file!
                // File time stamp headers: Since we are working with a stream here we do not know the local file's timestamps,
                // and then we can just omit the JCreated and JModified and let the server automatically set the current time.
                //req.Headers.Add("JCreated", timeCreated);
                //req.Headers.Add("JModified", timeModified);
                req.ContentType = "application/octet-stream";
                req.ContentLength = fileSize;
                // Write post data request
                var areq = new Utility.AsyncHttpRequest(req);
                using (var rs = areq.GetRequestStream())
                    Utility.Utility.CopyStream(stream, rs, true, m_copybuffer);
                // Send request, and check response
                using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    if (resp.StatusCode != System.Net.HttpStatusCode.Created)
                        throw new System.Net.WebException(Strings.Jottacloud.FileUploadError, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                    // Request seems to be successful, but we must verify the response XML content to be sure that the file
                    // was correctly uploaded: The server will verify the JSize header and mark the file as incomplete if
                    // there was mismatch, and it will verify the JMd5 header and mark the file as corrupt if there was a hash
                    // mismatch. The returned XML contains a file element, and if upload was error free it contains a single
                    // child element "currentRevision", which has a "state" child element with the string "COMPLETED".
                    // If there was a problem we should have a "latestRevision" child element, and this will have state with
                    // value "INCOMPLETE" or "CORRUPT". If the file was new or had no previous complete versions the latestRevision
                    // will be the only child, but if not there may also be a "currentRevision" representing the previous
                    // complete version - and then we need to detect the case where our upload failed but there was an existing
                    // complete version!
                    using (var rs = areq.GetResponseStream())
                    {
                        var doc = new System.Xml.XmlDocument();
                        try { doc.Load(rs); }
                        catch (System.Xml.XmlException)
                        {
                            throw new System.Net.WebException(Strings.Jottacloud.FileUploadError, System.Net.WebExceptionStatus.ProtocolError);
                        }
                        bool uploadCompletedSuccessfully = false;
                        var xFile = doc["file"];
                        if (xFile != null)
                        {
                            var xRevState = xFile.SelectSingleNode("latestRevision");
                            if (xRevState == null) {
                                xRevState = xFile.SelectSingleNode("currentRevision/state");
                                if (xRevState != null)
                                    uploadCompletedSuccessfully = xRevState.InnerText == "COMPLETED"; // Success: There is no "latestRevision", only a "currentRevision" (and it specifies the file is complete, but I think it always will).
                            }
                        }
                        if (!uploadCompletedSuccessfully) // Report error (and we just let the incomplete/corrupt file revision stay on the server..)
                            throw new System.Net.WebException(Strings.Jottacloud.FileUploadError, System.Net.WebExceptionStatus.ProtocolError);
                    }
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

        #endregion
    }
}
