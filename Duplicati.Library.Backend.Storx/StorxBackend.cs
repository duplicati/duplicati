using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Localization.Short;
using Newtonsoft.Json;
using System.Text;
using System.IO;
using System.Net.Http;
using static Duplicati.Library.Backend.Storx.Access;

namespace Duplicati.Library.Backend.Storx
{
    public class StorxBackend : IBackend, IStreamingBackend
    {
        private readonly string m_username = null;
        private readonly string m_password = null;
        private readonly string m_prefix = null;
        private readonly string authorization = null;
        private readonly string parent_folder = null;
        private string m_root_folder = null;
        private static readonly Encoding encoding = Encoding.UTF8;
        private const string root = "https://storx.io/api/";
        static StorxBackend()
        {
            System.Net.ServicePointManager.DefaultConnectionLimit = 1000;
        }

        public StorxBackend()
        {
        }

        public class LoginModel
        {
            [JsonProperty("sKey")]
            public string sKey { get; set; }
        }
        public class StorxFile
        {
            [JsonProperty("name")]
            public string name { get; set; }
            [JsonProperty("type")]
            public string type { get; set; }
            [JsonProperty("size")]
            public int size { get; set; }
            [JsonProperty("created_at")]
            public DateTime created_at { get; set; }
            [JsonProperty("updatedAt")]
            public DateTime updatedAt { get; set; }
            [JsonProperty("fileId")]
            public string fileId { get; set; }
            [JsonProperty("id")]
            public int id { get; set; }
        }

        public class StorxChild
        {
            [JsonProperty("name")]
            public string name { get; set; }
            [JsonProperty("parentId")]
            public int parentId { get; set; }
            [JsonProperty("id")]
            public int id { get; set; }
        }

        public class StorxFileList
        {
            [JsonProperty("files")]
            public List<StorxFile> Files { get; set; }
            [JsonProperty("name")]
            public string name { get; set; }
            [JsonProperty("created_at")]
            public string created_at { get; set; }
            [JsonProperty("children")]
            public List<StorxChild> Children { get; set; }
        }

        public StorxBackend(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            if (options.ContainsKey("auth-username"))
                m_username = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                m_password = options["auth-password"];

            if (!string.IsNullOrEmpty(uri.Username))
                m_username = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                m_password = uri.Password;

            if (string.IsNullOrEmpty(m_username))
                throw new UserInformationException(Strings.StorxBackend.NoUsernameError, "StorxNoUsername");
            if (string.IsNullOrEmpty(m_password))
                throw new UserInformationException(Strings.StorxBackend.NoPasswordError, "StorxNoPassword");

            m_prefix = uri.HostAndPath ?? "";
            if (string.IsNullOrEmpty(authorization) || string.IsNullOrEmpty(parent_folder))
            {
                authorization = GetToken().token;
                parent_folder = GetToken().user.root_folder_id;
            }
            if (!string.IsNullOrEmpty(authorization) && !string.IsNullOrEmpty(parent_folder))
            {
                if (m_root_folder == null & m_prefix != "")
                {
                    m_root_folder = GetFolder();
                }
                if (m_prefix == "")
                {
                    m_root_folder = parent_folder;
                }
            }

        }

        private AccessModel GetToken()
        {
            Access access = new Access();
            string endpoint = "login";
            var loginModel = new LoginModel();
            var accessModel= new AccessModel();
            try
            {
                System.Net.HttpWebRequest req = CreateRequest(endpoint);
                req.Method = System.Net.WebRequestMethods.Http.Post;
                string postData = "email=" + m_username + "";
                byte[] byteArray = Encoding.UTF8.GetBytes(postData);

                req.ContentType = "application/x-www-form-urlencoded";
                req.ContentLength = byteArray.Length;
                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);

                using (var rs = areq.GetRequestStream())
                    rs.Write(byteArray, 0, byteArray.Length);
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    Stream dataStream = resp.GetResponseStream();
                    StreamReader reader = new StreamReader(dataStream);
                    var responseFromServer = reader;
                    var serializer = new JsonSerializer();
                    var jr1 = new Newtonsoft.Json.JsonTextReader(responseFromServer);
                    loginModel = (LoginModel)serializer.Deserialize(jr1, typeof(LoginModel));
                    accessModel = access.accessApi(m_username, m_password, loginModel.sKey);
                }
            }
            catch (System.Net.WebException wex)
            {
                throw new Exception(getResponseBodyOnError(endpoint, wex));
            }
            return accessModel;
        }

        private System.Net.HttpWebRequest CreateRequest(string endpoint)
        {
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(root + endpoint);
            req.Accept = string.Format("*/*");
            req.Headers.Add("Authorization", "Bearer "+authorization);
            req.Headers.Add("storx-mnemonic", "scissors huge model dragon mind distance spider fit cute gym rain now dragon solution online monkey describe glow gossip cancel elder misery pony mask");
            req.KeepAlive = false;
            req.UserAgent = string.Format("Storx-Agent (Duplicati Storx client {0})", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
            return req;
        }

        private string getResponseBodyOnError(string context, System.Net.WebException wex)
        {
            HttpWebResponse response = wex.Response as HttpWebResponse;
            if (response is null)
            {
                return $"{context} failed with error: {wex.Message}";
            }

            string body = "";
            using (System.IO.Stream data = response.GetResponseStream())
            using (var reader = new System.IO.StreamReader(data))
            {
                body = reader.ReadToEnd();
            }
            return string.Format("{0} failed, response: {1}", context, body);
        }

        #region IBackend Members

        public string DisplayName
        {
            get { return Strings.StorxBackend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "storx"; }
        }


        public IEnumerable<IFileEntry> List()
        {

            StorxFileList fl;
            try
            {
                fl = GetFiles();
            }
            catch (System.Net.WebException wex)
            {
                throw new Exception("failed to Connect folder " + m_root_folder + wex.Message);
            }

            if (fl.Files != null)
            {
                foreach (var f in fl.Files)
                {
                    if (f.name != null)
                    {
                        FileEntry fe = new FileEntry(f.name + "." + f.type)
                        {
                            Size = f.size,
                            IsFolder = false,
                            LastModification = f.created_at,
                            LastAccess = f.created_at

                        };
                        yield return fe;
                    }
                }
            }
        }


        private StorxFileList GetFiles(int parent = 0)
        {
            var fl1 = new StorxFileList();
            string endpoint = "";
            if (m_root_folder == null && (m_prefix == "" || parent == 1))
            {

                endpoint = "storage/folder/" + parent_folder;
            }
            else
            {
                endpoint = "storage/folder/" + m_root_folder;
            }

            try
            {
                System.Net.HttpWebRequest req = CreateRequest(endpoint);
                req.Method = System.Net.WebRequestMethods.Http.Get;

                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);

                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {

                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300)
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                    Stream dataStream = resp.GetResponseStream();
                    StreamReader reader = new StreamReader(dataStream);
                    var responseFromServer = reader;
                    var serializer = new JsonSerializer();
                    var jr1 = new Newtonsoft.Json.JsonTextReader(responseFromServer);
                    fl1 = (StorxFileList)serializer.Deserialize(jr1, typeof(StorxFileList));
                }
            }
            catch (System.Net.WebException wex)
            {
                throw new Exception(getResponseBodyOnError(endpoint, wex));
            }
            return fl1;
        }


        private string GetFolder(int create = 0)
        {
            StorxFileList fl2 = GetFiles(1);
            string endpoint = "storage/folder";
            if (fl2.Children != null)
            {
                List<StorxChild> folder = fl2.Children.FindAll(x => x.name == m_prefix);
                if (folder.Count <= 0)
                {
                    System.Net.HttpWebRequest req = CreateRequest(endpoint);
                    req.Method = System.Net.WebRequestMethods.Http.Post;
                    string postData = "folderName=" + m_prefix + "";
                    postData += "&parentFolderId="+parent_folder+"";
                    postData += "&teamId=";
                    byte[] byteArray = Encoding.UTF8.GetBytes(postData);

                    req.ContentType = "application/x-www-form-urlencoded";
                    req.ContentLength = byteArray.Length;
                    Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);

                    using (var rs = areq.GetRequestStream())
                        rs.Write(byteArray, 0, byteArray.Length);
                    using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                    {
                        Stream dataStream = resp.GetResponseStream();
                        StreamReader reader = new StreamReader(dataStream);
                        var responseFromServer = reader;
                        var serializer = new JsonSerializer();
                        var jr1 = new Newtonsoft.Json.JsonTextReader(responseFromServer);
                        StorxChild flc = (StorxChild)serializer.Deserialize(jr1, typeof(StorxChild));
                        m_root_folder = Convert.ToString(flc.id);
                    }
                }
                else
                {
                    m_root_folder = Convert.ToString(folder[0].id);
                }
            }
            return m_root_folder;
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
            StorxFileList fl1 = GetFiles();
            if (fl1.Files != null)
            {
                var fileID = fl1.Files.FindAll(x => (x.name + "." + x.type) == remotename)[0];
                string endpoint = root+"storage/folder/" + m_root_folder + "/file/" + fileID.id;
                try
                {
                    WebRequest request = WebRequest.Create(endpoint);
                    request.Method = "DELETE";
                    request.Headers.Add("Authorization", "Bearer " + authorization);
                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                }
                catch (System.Net.WebException wex)
                {
                    if (wex.Response is HttpWebResponse response && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        throw new FileMissingException(wex);
                    else
                        throw new Exception(getResponseBodyOnError(endpoint, wex));
                }
            }

        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.StorxBackend.DescriptionAuthPasswordShort, Strings.StorxBackend.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.StorxBackend.DescriptionAuthUsernameShort, Strings.StorxBackend.DescriptionAuthUsernameLong)
                });
            }
        }

        public string Description
        {
            get { return Strings.StorxBackend.Description; }
        }

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
           // GetFolder(1);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion


        public string[] DNSName
        {
            get { return new string[] { new System.Uri(root).Host }; }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            StorxFileList fl1 = GetFiles();
            if (fl1.Files != null)
            {
                var fileID = fl1.Files.FindAll(x => (x.name + "." + x.type) == remotename)[0];
                string endpoint = "storage/file/" + fileID.fileId;
                var req = CreateRequest(endpoint);
                req.Method = System.Net.WebRequestMethods.Http.Get;
                var rreq = new Utility.AsyncHttpRequest(req);
                using (var s = rreq.GetResponseStream())
                    Library.Utility.Utility.CopyStream(s, stream);
            }

        }

        public Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            string endpoint = root+"storage/folder/" + m_root_folder + "/upload";
            try
            {
                byte[] formData = ReadToEnd(stream);
                var webClient = new WebClient();
                string boundary = "------------------------" + DateTime.Now.Ticks.ToString("x");
                string contentType = "multipart/form-data; boundary=" + boundary;
                webClient.Headers.Add("Content-Type", "multipart/form-data; boundary=" + boundary);
                webClient.Headers.Add("Authorization", "Bearer " + authorization);
                webClient.Headers.Add("storx-mnemonic", "scissors huge model dragon mind distance spider fit cute gym rain now dragon solution online monkey describe glow gossip cancel elder misery pony mask");
                var fileData = webClient.Encoding.GetString(formData);
                var package = string.Format("--{0}\r\nContent-Disposition: form-data; name=\"xfile\"; filename=\"{1}\"\r\nContent-Type: {2}\r\n\r\n{3}\r\n--{0}--\r\n", boundary, remotename, contentType, fileData);
                var nfile = webClient.Encoding.GetBytes(package);
                byte[] resp = webClient.UploadData(endpoint, "POST", nfile);
                return Task.FromResult(true);
            }

            catch (System.Net.WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                else
                    throw new Exception(getResponseBodyOnError(endpoint, wex));
            }
            
        }

        public static byte[] ReadToEnd(System.IO.Stream stream)
        {
            long originalPosition = 0;
            if (stream.CanSeek)
            {
                originalPosition = stream.Position;
                stream.Position = 0;
            }
            try
            {
                byte[] readBuffer = new byte[4096];
                int totalBytesRead = 0;
                int bytesRead;
                while ((bytesRead = stream.Read(readBuffer, totalBytesRead, readBuffer.Length - totalBytesRead)) > 0)
                {
                    totalBytesRead += bytesRead;

                    if (totalBytesRead == readBuffer.Length)
                    {
                        int nextByte = stream.ReadByte();
                        if (nextByte != -1)
                        {
                            byte[] temp = new byte[readBuffer.Length * 2];
                            Buffer.BlockCopy(readBuffer, 0, temp, 0, readBuffer.Length);
                            Buffer.SetByte(temp, totalBytesRead, (byte)nextByte);
                            readBuffer = temp;
                            totalBytesRead++;
                        }
                    }
                }

                byte[] buffer = readBuffer;
                if (readBuffer.Length != totalBytesRead)
                {
                    buffer = new byte[totalBytesRead];
                    Buffer.BlockCopy(readBuffer, 0, buffer, 0, totalBytesRead);
                }
                return buffer;
            }
            finally
            {
                if (stream.CanSeek)
                {
                    stream.Position = originalPosition;
                }
            }
        }
    }
}