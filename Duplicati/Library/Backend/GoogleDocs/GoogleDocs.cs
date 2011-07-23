using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Web;
using System.Xml;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class GoogleDocs : IBackend_v2, IStreamingBackend, IBackendGUI
    {
        private const string USERNAME_OPTION = "google-username";
        private const string PASSWORD_OPTION = "google-password";
        private const string ATTRIBUTES_OPTION = "google-labels";
        private static readonly string[] KNOWN_LABELS = new string[] { "starred", "viewed", "hidden" };

        private static readonly Regex URL_PARSER = new Regex("googledocs://(?<folder>.*)", RegexOptions.IgnoreCase);
        private static readonly string USER_AGENT = "Duplicati GoogleDocs backend v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

        private const string CREATE_ITEM_TEMPLATE = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><entry xmlns=\"http://www.w3.org/2005/Atom\" xmlns:docs=\"http://schemas.google.com/docs/2007\"><category scheme=\"http://schemas.google.com/g/2005#kind\" term=\"http://schemas.google.com/docs/2007#document\"/><title>{0}</title><docs:writersCanInvite value=\"false\"/>{1}</entry>";
        private const string ATTRIBUTE_TEMPLATE = "<category scheme=\"http://schemas.google.com/g/2005/labels\" term=\"http://schemas.google.com/g/2005/labels#{0}\" label=\"{0}\"/>";

        private const string DEFAULT_LABELS = "viewed,hidden";


        //Seems the Google limit is 10MB
        private const long TRANSFER_CHUNK_SIZE = 1024 * 1024 * 5; //5MB Chuncks

        private string m_username;
        private string m_password;
        private string m_folder;
        private string[] m_labels;

        private Dictionary<string, Google.Documents.Document> m_folders = null;
        private Dictionary<string, TaggedFileEntry> m_files = null;

        public GoogleDocs() { }

        public GoogleDocs(string url, Dictionary<string, string> options)
        {
            Match m = URL_PARSER.Match(url);
            if (!m.Success)
                throw new Exception(string.Format(Strings.GoogleDocs.InvalidUrlError, url));

            m_folder = m.Groups["folder"].Value;
            if (m_folder.EndsWith("/"))
                m_folder = m_folder.Substring(0, m_folder.Length - 1);

            if (options.ContainsKey("ftp-username"))
                m_username = options["ftp-username"];
            if (options.ContainsKey("ftp-password"))
                m_password = options["ftp-password"];
            if (options.ContainsKey(USERNAME_OPTION))
                m_username = options[USERNAME_OPTION];
            if (options.ContainsKey(PASSWORD_OPTION))
                m_password = options[PASSWORD_OPTION];

            string labels;
            if (!options.TryGetValue(ATTRIBUTES_OPTION, out labels))
                labels = DEFAULT_LABELS;

            m_labels = (labels ?? "").Split(new string[] {","}, StringSplitOptions.RemoveEmptyEntries);

            if (string.IsNullOrEmpty(m_username))
                throw new Exception(Strings.GoogleDocs.MissingUsernameError);
            if (string.IsNullOrEmpty(m_password))
                throw new Exception(Strings.GoogleDocs.MissingPasswordError);
        }

        private Google.GData.Documents.DocumentsService CreateService()
        {
            Google.GData.Documents.DocumentsService s = new Google.GData.Documents.DocumentsService(USER_AGENT);
            s.Credentials = new Google.GData.Client.GDataCredentials(m_username, m_password);
            return s;
        }

        private Google.Documents.DocumentsRequest CreateRequest()
        {
            Google.GData.Client.RequestSettings settings = new Google.GData.Client.RequestSettings(USER_AGENT, m_username, m_password);
            settings.AutoPaging = true;
            Google.Documents.DocumentsRequest req = new Google.Documents.DocumentsRequest(settings);

            return req;
        }

        private Google.Documents.Document GetFolder()
        {
            Google.Documents.Document res;
            if (m_folders != null && m_folders.TryGetValue(m_folder, out res))
                return res;

            m_folders = ParseFolderTree(CreateRequest().GetFolders().Entries);

            if (m_folders.TryGetValue(m_folder, out res))
                return res;

            throw new FolderMissingException();
        }

        private Google.Documents.Document GetFile(string filename)
        {
            TaggedFileEntry res;
            if (m_files != null && m_files.TryGetValue(filename, out res))
                return res.Doc;

            m_files = GetFileList();

            if(m_files.TryGetValue(filename, out res))
                return res.Doc;

            throw new System.IO.FileNotFoundException(filename);
        }

        private Dictionary<string, TaggedFileEntry> GetFileList()
        {
            Google.Documents.Document folder = GetFolder();
            Google.Documents.DocumentsRequest req = CreateRequest();
            Dictionary<string, TaggedFileEntry> results = new Dictionary<string, TaggedFileEntry>();

            foreach (Google.Documents.Document file in req.GetFolderContent(folder).Entries)
                results.Add(file.Title, new TaggedFileEntry(file.Title, file.QuotaBytesUsed, file.LastViewed, file.Updated, file));
            
            return results;
        }

        private static Dictionary<string, Google.Documents.Document> ParseFolderTree(IEnumerable<Google.Documents.Document> data)
        {
            Dictionary<string, Google.Documents.Document> parentlookup = new Dictionary<string, Google.Documents.Document>();
            Dictionary<string, Google.Documents.Document> dict = new Dictionary<string, Google.Documents.Document>();

            foreach (Google.Documents.Document d in data)
                parentlookup.Add(d.AtomEntry.EditUri.Content, d);

            foreach (Google.Documents.Document d in parentlookup.Values)
            {
                string[] p = new string[d.ParentFolders.Count + 1];
                for (int i = 0; i < p.Length - 1; i++)
                    p[i] = parentlookup[d.ParentFolders[i]].Title;

                p[p.Length - 1] = d.Title;
                dict.Add(string.Join("/", p), d);
            }
            
            return dict;
        }


        #region IBackend_v2 Members

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            Dictionary<string, Google.Documents.Document> docs = ParseFolderTree(CreateRequest().GetFolders().Entries);

            string[] subfolders = m_folder.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            string curfolder = "";
            Google.Documents.Document parentfolder = null;
            foreach (string s in subfolders)
            {
                if (curfolder.Length == 0)
                    curfolder = s;
                else
                    curfolder = curfolder + "/" + s;

                if (!docs.ContainsKey(curfolder))
                {
                    Google.Documents.Document doc = new Google.Documents.Document();
                    doc.Title = s;
                    doc.DocumentEntry.IsFolder = true;

                    Google.Documents.Document d;
                    if (parentfolder == null)
                        d = CreateRequest().CreateDocument(doc);
                    else
                        d = CreateRequest().Insert<Google.Documents.Document>(new Uri(Google.GData.Documents.DocumentsListQuery.documentsBaseUri + "/" + System.Web.HttpUtility.UrlEncode(parentfolder.ResourceId) + "/contents"), doc);
                    docs.Add(curfolder, d);
                }

                parentfolder = docs[curfolder];
            }
        }

        #endregion

        #region IBackend Members

        public string DisplayName
        {
            get { return Strings.GoogleDocs.Displayname; }
        }

        public string ProtocolKey
        {
            get { return "googledocs"; }
        }

        public List<IFileEntry> List()
        {
            m_files = GetFileList();

            List<IFileEntry> results = new List<IFileEntry>();
            foreach (TaggedFileEntry e in m_files.Values)
                results.Add(e);

            return results;
        }

        public void Put(string remotename, string filename)
        {
            //Google.GData.Documents.DocumentEntry de = CreateService().UploadFile(filename, remotename, "", false);

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
            try
            {
                CreateRequest().Delete(new Uri(GetFile(remotename).AtomEntry.EditUri.Content + "?delete=true"), "*");

                //We need to ensure that a LIST will not return the removed file
                m_files.Remove(remotename);
            }
            catch
            {
                //We have no idea if the file was removed or not,
                // so we force a filelist reload
                m_files = null;
                throw;
            }
        }        

        public IList<ICommandLineArgument> SupportedCommands
        {
            get {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.String, Strings.GoogleDocs.DescriptionFTPPasswordShort, Strings.GoogleDocs.DescriptionFTPPasswordLong),
                    new CommandLineArgument("ftp-username", CommandLineArgument.ArgumentType.String, Strings.GoogleDocs.DescriptionFTPUsernameShort, Strings.GoogleDocs.DescriptionFTPUsernameLong),
                    new CommandLineArgument(USERNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleDocs.DescriptionGooglePasswordShort, Strings.GoogleDocs.DescriptionGooglePasswordLong, null, new string[] {"ftp-password"}),
                    new CommandLineArgument(PASSWORD_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleDocs.DescriptionGoogleUsernameShort, Strings.GoogleDocs.DescriptionGoogleUsernameLong, null, new string[] {"ftp-username"}),
                    new CommandLineArgument(ATTRIBUTES_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleDocs.DescriptionGoogleLabelsShort, string.Format(Strings.GoogleDocs.DescriptionGoogleLabelsLong, string.Join(",", KNOWN_LABELS)), DEFAULT_LABELS),
                });
            }
        }

        public string Description
        {
            get { return Strings.GoogleDocs.Description; }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        #region IStreamingBackend Members

        public void Put(string remotename, System.IO.Stream stream)
        {
            Google.Documents.Document folder = GetFolder();
            Google.GData.Client.ClientLoginAuthenticator cla = new Google.GData.Client.ClientLoginAuthenticator(USER_AGENT, Google.GData.Client.ServiceNames.Documents, m_username, m_password);

            //First we need to get a resumeable upload url
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://docs.google.com/feeds/upload/create-session/default/private/full/" + System.Web.HttpUtility.UrlEncode(folder.ResourceId) + "/contents?convert=false");
            req.Method = "POST";
            req.Headers.Add("X-Upload-Content-Length", stream.Length.ToString());
            req.Headers.Add("X-Upload-Content-Type", "application/octet-stream");
            req.UserAgent = USER_AGENT;
            req.Headers.Add("GData-Version", "3.0");

            //Build the atom entry describing the file we want to create
            string labels = "";
            foreach(string s in m_labels)
                if (s.Trim().Length > 0)
                    labels += string.Format(ATTRIBUTE_TEMPLATE, s);
            
            //Apply the name and content-type to the not-yet-uploaded file
            byte[] data = System.Text.Encoding.UTF8.GetBytes(string.Format(CREATE_ITEM_TEMPLATE, System.Web.HttpUtility.HtmlEncode(remotename), labels));
            req.ContentLength = data.Length;
            req.ContentType = "application/atom+xml";

            //Authenticate our request
            cla.ApplyAuthenticationToRequest(req);

            using (System.IO.Stream s = req.GetRequestStream())
                s.Write(data, 0, data.Length);

            string newUri;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                    throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                newUri = resp.Headers["Location"];
            }

            //Ensure that we have a resumeable upload url
            if (newUri == null)
                throw new Exception(Strings.GoogleDocs.NoResumeURLError);

            string id = null;
            byte[] buffer = new byte[8 * 1024];

            while (stream.Position != stream.Length)
            {
                long postbytes = Math.Min(stream.Length - stream.Position, TRANSFER_CHUNK_SIZE);

                //Post a fragment of the file as a partial request
                req = (HttpWebRequest)WebRequest.Create(newUri);
                req.Method = "PUT";
                req.UserAgent = USER_AGENT;
                req.ContentLength = postbytes;
                req.ContentType = "application/octet-stream";
                req.Headers.Add("Content-Range", string.Format("bytes {0}-{1}/{2}", stream.Position, stream.Position + (postbytes - 1), stream.Length.ToString()));

                //Copy the current fragment of bytes
                using (System.IO.Stream s = req.GetRequestStream())
                {
                    long bytesleft = postbytes;
                    long written = 0;
                    int a;
                    while (bytesleft != 0 && ((a = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, bytesleft))) != 0))
                    {
                        s.Write(buffer, 0, a);
                        bytesleft -= a;
                        written += a;
                    }

                    s.Flush();

                    if (bytesleft != 0 || postbytes != written)
                        throw new System.IO.EndOfStreamException();
                }

                try
                {
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        int code = (int)resp.StatusCode;
                        if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                            throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                        //If all goes well, we should now get an atom entry describing the new element
                        System.Xml.XmlDocument doc = new XmlDocument();
                        using (System.IO.Stream s = resp.GetResponseStream())
                            doc.Load(s);

                        System.Xml.XmlNamespaceManager mgr = new XmlNamespaceManager(doc.NameTable);
                        mgr.AddNamespace("atom", "http://www.w3.org/2005/Atom");
                        id = doc.SelectSingleNode("atom:entry/atom:id", mgr).InnerText;
                    }
                }
                catch (WebException wex)
                {
                    //Accept the 308 until we are complete
                    if (wex.Status == WebExceptionStatus.ProtocolError && 
                        wex.Response is HttpWebResponse && 
                        (int)((HttpWebResponse)wex.Response).StatusCode == 308 && 
                        stream.Position != stream.Length)
                    {
                        //Accept the 308 until we are complete
                    }
                    else
                        throw;

                }
            }

            if (string.IsNullOrEmpty(id))
                throw new Exception(Strings.GoogleDocs.NoIDReturnedError);
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            using (System.IO.Stream s = CreateRequest().Download(GetFile(remotename), null))
                Utility.Utility.CopyStream(s, stream);
        }

        #endregion

        #region IGUIControl Members

        public string PageTitle
        {
            get { return GoogleDocsUI.PageTitle; }
        }

        public string PageDescription
        {
            get { return GoogleDocsUI.PageDescription; }
        }

        public System.Windows.Forms.Control GetControl(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
        {
            return new GoogleDocsUI(options);
        }

        public void Leave(System.Windows.Forms.Control control)
        {
            ((GoogleDocsUI)control).Save(false);
        }

        public bool Validate(System.Windows.Forms.Control control)
        {
            return ((GoogleDocsUI)control).Save(true);
        }

        public string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            return GoogleDocsUI.GetConfiguration(guiOptions, commandlineOptions);
        }

        #endregion
    }
}
