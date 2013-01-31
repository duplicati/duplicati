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
        private const string CAPTCHA_UNLOCK_URL = "https://www.google.com/accounts/UnlockCaptcha";

        //Seems the Google limit is 10MB
        private const long TRANSFER_CHUNK_SIZE = 1024 * 1024 * 5; //5MB Chuncks (must be divisible by 512)

        private string m_folder;
        private string[] m_labels;

        private Dictionary<string, Google.Documents.Document> m_folders = null;
        private Dictionary<string, TaggedFileEntry> m_files = null;
        private Google.GData.Client.RequestSettings m_settings = null;
        private Google.GData.Client.ClientLoginAuthenticator m_cla;
        private Google.GData.Client.Service m_service;

        public GoogleDocs() { }

        public GoogleDocs(string url, Dictionary<string, string> options)
        {
            Match m = URL_PARSER.Match(url);
            if (!m.Success)
                throw new Exception(string.Format(Strings.GoogleDocs.InvalidUrlError, url));

            m_folder = m.Groups["folder"].Value;
            if (m_folder.EndsWith("/"))
                m_folder = m_folder.Substring(0, m_folder.Length - 1);

            string username = null;
            string password = null;

            if (options.ContainsKey("ftp-username"))
                username = options["ftp-username"];
            if (options.ContainsKey("ftp-password"))
                password = options["ftp-password"];
            if (options.ContainsKey(USERNAME_OPTION))
                username = options[USERNAME_OPTION];
            if (options.ContainsKey(PASSWORD_OPTION))
                password = options[PASSWORD_OPTION];

            string labels;
            if (!options.TryGetValue(ATTRIBUTES_OPTION, out labels))
                labels = DEFAULT_LABELS;

            m_labels = (labels ?? "").Split(new string[] {","}, StringSplitOptions.RemoveEmptyEntries);

            if (string.IsNullOrEmpty(username))
                throw new Exception(Strings.GoogleDocs.MissingUsernameError);
            if (string.IsNullOrEmpty(password))
                throw new Exception(Strings.GoogleDocs.MissingPasswordError);

            m_cla = new Google.GData.Client.ClientLoginAuthenticator(USER_AGENT, Google.GData.Client.ServiceNames.Documents, username, password);

            m_settings = new Google.GData.Client.RequestSettings(USER_AGENT, username, password);
            m_settings.AutoPaging = true;
            m_service = new Google.GData.Client.Service();
        }

        private Google.Documents.DocumentsRequest CreateRequest()
        {
            return new Google.Documents.DocumentsRequest(m_settings);
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

        private TaggedFileEntry TryGetFile(string filename)
        {
            TaggedFileEntry res;
            if (m_files != null && m_files.TryGetValue(filename, out res))
                return res;

            m_files = GetFileList();

            if (m_files.TryGetValue(filename, out res))
                return res;

            return null;
        }

        private TaggedFileEntry GetFile(string filename)
        {
            TaggedFileEntry res = TryGetFile(filename);
            
            if (res == null)
                throw new System.IO.FileNotFoundException(filename);
            
            return res;
        }

        private Dictionary<string, TaggedFileEntry> GetFileList()
        {
            Google.Documents.Document folder = GetFolder();
            Google.Documents.DocumentsRequest req = CreateRequest();
            Dictionary<string, TaggedFileEntry> results = new Dictionary<string, TaggedFileEntry>();

            foreach (Google.Documents.Document file in req.GetFolderContent(folder).Entries)
            {
                if (results.ContainsKey(file.Title))
                    throw new Exception(string.Format(Strings.GoogleDocs.DuplicateFilenameFoundError, file.Title, folder.Title));

                string updateUrl = null;
                foreach (Google.GData.Client.AtomLink x in file.AtomEntry.Links)
                    if (x.Rel.EndsWith("#resumable-edit-media"))
                    {
                        updateUrl = x.HRef.ToString();
                        break;
                    }

                results.Add(file.Title, new TaggedFileEntry(file.Title, (long)file.QuotaBytesUsed, file.LastViewed, file.Updated, file.ResourceId, file.DocumentEntry.Content.Src.ToString(), updateUrl, file.ETag));
            }
            return results;
        }

        private static Dictionary<string, Google.Documents.Document> ParseFolderTree(IEnumerable<Google.Documents.Document> data)
        {
            Dictionary<string, Google.Documents.Document> parentlookup = new Dictionary<string, Google.Documents.Document>();
            Dictionary<string, Google.Documents.Document> dict = new Dictionary<string, Google.Documents.Document>();

            foreach (Google.Documents.Document d in data)
            {
                // note: some files have no Edit Uri (for ex. shared by different user)
                if (d.AtomEntry.EditUri != null && d.AtomEntry.EditUri.Content != null)
                {
                    parentlookup.Add(d.AtomEntry.EditUri.Content, d);
                }
            }				

            foreach (Google.Documents.Document d in parentlookup.Values)
            {
                List<Google.Documents.Document> parents = new List<Google.Documents.Document>();
                Google.Documents.Document cur = d;
                while (cur.ParentFolders.Count == 1)
                {
                    parents.Add(cur);
                    cur = parentlookup[cur.ParentFolders[0]];
                }
                parents.Add(cur);

                if (cur.ParentFolders.Count != 0)
                {
                    string[] pids = new string[cur.ParentFolders.Count];
                    for(int i = 0; i < pids.Length; i++)
                        pids[i] = parentlookup[cur.ParentFolders[i]].Title;

                    throw new Exception(string.Format(Strings.GoogleDocs.FolderHasMultipleOwnersError, cur.Title, string.Join(", ", pids)));
                }

                parents.Reverse();
                string[] p = new string[parents.Count];
                for (int i = 0; i < p.Length; i++)
                    p[i] = parents[i].Title;

                string key = string.Join("/", p);
                if (dict.ContainsKey(key))
                    throw new Exception(string.Format(Strings.GoogleDocs.DuplicateFoldernameFoundError, key));
                dict.Add(key, d);
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
            Dictionary<string, Google.GData.Client.AtomCategory> categories = new Dictionary<string,Google.GData.Client.AtomCategory>();

            //We know of these labels statically
            categories["starred"] = Google.GData.Documents.DocumentEntry.STARRED_CATEGORY;
            categories["viewed"] = Google.GData.Documents.DocumentEntry.VIEWED_CATEGORY;
            categories["hidden"] = Google.GData.Documents.DocumentEntry.HIDDEN_CATEGORY;

            try 
            {
                //Pick up any extra supported labels by reflection
                foreach(System.Reflection.FieldInfo fi in typeof(Google.GData.Documents.DocumentEntry).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    if (fi.FieldType == typeof(Google.GData.Client.AtomCategory))  
                    {
                        Google.GData.Client.AtomCategory cat = (Google.GData.Client.AtomCategory)fi.GetValue(null);
                        if (cat.Scheme == "http://schemas.google.com/g/2005/labels")
                            categories[cat.Label] = cat;
                    }
            } 
            catch 
            {
                //Extra labels are non-fatal
            }

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

                    foreach (string label in m_labels) 
                        if (categories.ContainsKey(label))
                            doc.DocumentEntry.ToggleCategory(categories[label], true);

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
            try
            {
                m_files = GetFileList();

                List<IFileEntry> results = new List<IFileEntry>();
                foreach (TaggedFileEntry e in m_files.Values)
                    results.Add(e);

                return results;
            } 
            catch (Google.GData.Client.CaptchaRequiredException cex) 
            {
                throw new Exception(string.Format(Strings.GoogleDocs.CaptchaRequiredError, CAPTCHA_UNLOCK_URL), cex);
            }
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
            try
            {
                TaggedFileEntry f = GetFile(remotename);

                //This does not work if the item is in a folder, because it will only be removed from the folder,
                // even if with the "delete=true" setting
                //CreateRequest().Delete(new Uri(f.AtomEntry.EditUri.Content + "?delete=true"), f.ETag);

                //Instead we create the root element id (that is without any folder information),
                //and delete that instead, that seems to works as desired, fully removing the file
                Google.Documents.DocumentsRequest req = CreateRequest();
                string url = req.BaseUri + "/" + HttpUtility.UrlEncode(f.ResourceId) + "?delete=true";
                req.Delete(new Uri(url), f.ETag);

                //We need to ensure that a LIST will not return the removed file
                m_files.Remove(remotename);
            }
            catch (Google.GData.Client.CaptchaRequiredException cex)
            {
                throw new Exception(string.Format(Strings.GoogleDocs.CaptchaRequiredError, CAPTCHA_UNLOCK_URL), cex);
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
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.Password, Strings.GoogleDocs.DescriptionFTPPasswordShort, Strings.GoogleDocs.DescriptionFTPPasswordLong),
                    new CommandLineArgument("ftp-username", CommandLineArgument.ArgumentType.String, Strings.GoogleDocs.DescriptionFTPUsernameShort, Strings.GoogleDocs.DescriptionFTPUsernameLong),
                    new CommandLineArgument(USERNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.GoogleDocs.DescriptionGooglePasswordShort, Strings.GoogleDocs.DescriptionGooglePasswordLong, null, new string[] {"ftp-password"}),
                    new CommandLineArgument(PASSWORD_OPTION, CommandLineArgument.ArgumentType.Password, Strings.GoogleDocs.DescriptionGoogleUsernameShort, Strings.GoogleDocs.DescriptionGoogleUsernameLong, null, new string[] {"ftp-username"}),
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
            m_settings = null;
            m_cla = null;
        }

        #endregion

        #region IStreamingBackend Members

        public void Put(string remotename, System.IO.Stream stream)
        {
            try
            {
                
                Google.Documents.Document folder = GetFolder();
                
                //Special, since uploads can overwrite or create,
                // we must figure out if the file exists in advance.
                //Unfortunately it would be wastefull to request the list 
                // for each upload request, so we rely on the cache being
                // correct

                TaggedFileEntry doc = null;
                if (m_files == null)
                    doc = TryGetFile(remotename);
                else 
                    m_files.TryGetValue(remotename, out doc);

                try
                {
                    string resumableUri;
                    if (doc != null)
                    {
                        if (doc.MediaUrl == null)
                        {
                            //Strange, we could not get the edit url, perhaps it is readonly?
                            //Fallback strategy is "delete-then-upload"
                            try { this.Delete(remotename); }
                            catch { }

                            doc = TryGetFile(remotename);
                            if (doc != null || doc.MediaUrl == null)
                                throw new Exception(string.Format(Strings.GoogleDocs.FileIsReadOnlyError, remotename));
                        }
                    }


                    //File does not exist, we upload a new one
                    if (doc == null)
                    {
                        //First we need to get a resumeable upload url
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://docs.google.com/feeds/upload/create-session/default/private/full/" + System.Web.HttpUtility.UrlEncode(folder.ResourceId) + "/contents?convert=false");
                        req.Method = "POST";
                        req.Headers.Add("X-Upload-Content-Length", stream.Length.ToString());
                        req.Headers.Add("X-Upload-Content-Type", "application/octet-stream");
                        req.UserAgent = USER_AGENT;
                        req.Headers.Add("GData-Version", "3.0");

                        //Build the atom entry describing the file we want to create
                        string labels = "";
                        foreach (string s in m_labels)
                            if (s.Trim().Length > 0)
                                labels += string.Format(ATTRIBUTE_TEMPLATE, s);

                        //Apply the name and content-type to the not-yet-uploaded file
                        byte[] data = System.Text.Encoding.UTF8.GetBytes(string.Format(CREATE_ITEM_TEMPLATE, System.Web.HttpUtility.HtmlEncode(remotename), labels));
                        req.ContentLength = data.Length;
                        req.ContentType = "application/atom+xml";

                        //Authenticate our request
                        m_cla.ApplyAuthenticationToRequest(req);

                        using (System.IO.Stream s = req.GetRequestStream())
                            s.Write(data, 0, data.Length);

                        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                        {
                            int code = (int)resp.StatusCode;
                            if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                                throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                            resumableUri = resp.Headers["Location"];
                        }
                    }
                    else
                    {
                        //First we need to get a resumeable upload url
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(doc.MediaUrl);
                        req.Method = "PUT";
                        req.Headers.Add("X-Upload-Content-Length", stream.Length.ToString());
                        req.Headers.Add("X-Upload-Content-Type", "application/octet-stream");
                        req.UserAgent = USER_AGENT;
                        req.Headers.Add("If-Match", doc.ETag);
                        req.Headers.Add("GData-Version", "3.0");

                        //This is a blank marker request
                        req.ContentLength = 0;
                        //Bad... docs say "text/plain" or "text/xml", but really needs to be content type, otherwise overwrite fails
                        //req.ContentType = "text/plain";
                        req.ContentType = "application/octet-stream";

                        //Authenticate our request
                        m_cla.ApplyAuthenticationToRequest(req);

                        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                        {
                            int code = (int)resp.StatusCode;
                            if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                                throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                            resumableUri = resp.Headers["Location"];
                        }
                    }

                    //Ensure that we have a resumeable upload url
                    if (resumableUri == null)
                        throw new Exception(Strings.GoogleDocs.NoResumeURLError);

                    string id = null;
                    byte[] buffer = new byte[8 * 1024];
                    int retries = 0;
                    long initialPosition;
                    DateTime initialRequestTime = DateTime.Now;

                    while (stream.Position != stream.Length)
                    {
                        initialPosition = stream.Position;
                        long postbytes = Math.Min(stream.Length - initialPosition, TRANSFER_CHUNK_SIZE);

                        //Post a fragment of the file as a partial request
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(resumableUri);
                        req.Method = "PUT";
                        req.UserAgent = USER_AGENT;
                        req.ContentLength = postbytes;
                        req.ContentType = "application/octet-stream";
                        req.Headers.Add("Content-Range", string.Format("bytes {0}-{1}/{2}", initialPosition, initialPosition + (postbytes - 1), stream.Length.ToString()));

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
                                System.Xml.XmlDocument xml = new XmlDocument();
                                using (System.IO.Stream s = resp.GetResponseStream())
                                    xml.Load(s);

                                System.Xml.XmlNamespaceManager mgr = new XmlNamespaceManager(xml.NameTable);
                                mgr.AddNamespace("atom", "http://www.w3.org/2005/Atom");
                                mgr.AddNamespace("gd", "http://schemas.google.com/g/2005");
                                id = xml.SelectSingleNode("atom:entry/atom:id", mgr).InnerText;
                                string resourceId = xml.SelectSingleNode("atom:entry/gd:resourceId", mgr).InnerText;
                                string url = xml.SelectSingleNode("atom:entry/atom:content", mgr).Attributes["src"].Value;
                                string mediaUrl = null;

                                foreach(XmlNode n in xml.SelectNodes("atom:entry/atom:link", mgr))
                                    if (n.Attributes["rel"] != null && n.Attributes["href"] != null &&n.Attributes["rel"].Value.EndsWith("#resumable-edit-media")) 
                                    {
                                        mediaUrl = n.Attributes["href"].Value;
                                        break;
                                    }

                                if (doc == null)
                                {
                                    TaggedFileEntry tf = new TaggedFileEntry(remotename, stream.Length, initialRequestTime, initialRequestTime, resourceId, url, mediaUrl, resp.Headers["ETag"]);
                                    m_files.Add(remotename, tf);
                                }
                                else
                                {

                                    //Since we update an existing item, we just need to update the ETag
                                    doc.ETag = resp.Headers["ETag"];
                                }


                            }
                            retries = 0;
                        }
                        catch (WebException wex)
                        {
							bool acceptedError =
								wex.Status == WebExceptionStatus.ProtocolError &&
                                wex.Response is HttpWebResponse &&
                                (int)((HttpWebResponse)wex.Response).StatusCode == 308;

							//Mono does not give us the response object,
							// so we rely on the error code being present
							// in the string, not ideal, but I have found
							// no other workaround :(
							if (Duplicati.Library.Utility.Utility.IsMono)
							{
								acceptedError |= 
									wex.Status == WebExceptionStatus.ProtocolError &&
									wex.Message.Contains("308");
							}

                            //Accept the 308 until we are complete
                            if (acceptedError &&
                                initialPosition + postbytes != stream.Length)
                            {
                                retries = 0;
                                //Accept the 308 until we are complete
                            }
                            else
                            {
                                //Retries are handled in Duplicati, but it is much more efficient here,
                                // because we only re-submit the last TRANSFER_CHUNK_SIZE bytes,
                                // instead of the entire file
                                retries++;
                                if (retries > 2)
                                    throw;
                                else
                                    System.Threading.Thread.Sleep(2000 * retries);

                                stream.Position = initialPosition;
                            }

                        }
                    }

                    if (string.IsNullOrEmpty(id))
                        throw new Exception(Strings.GoogleDocs.NoIDReturnedError);
                }
                catch
                {
                    //Clear the cache as we have no idea what happened
                    m_files = null;

                    throw;
                }
            }
            catch (Google.GData.Client.CaptchaRequiredException cex)
            {
                throw new Exception(string.Format(Strings.GoogleDocs.CaptchaRequiredError, CAPTCHA_UNLOCK_URL), cex);
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            try
            {
                using (System.IO.Stream s = CreateRequest().Service.Query(new Uri(GetFile(remotename).Url)))
                    Utility.Utility.CopyStream(s, stream);
            }
            catch (Google.GData.Client.CaptchaRequiredException cex)
            {
                throw new Exception(string.Format(Strings.GoogleDocs.CaptchaRequiredError, CAPTCHA_UNLOCK_URL), cex);
            }
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
