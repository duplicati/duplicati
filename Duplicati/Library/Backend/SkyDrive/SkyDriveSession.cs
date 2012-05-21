 using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Xml;
using Duplicati.Library.Interface;
using System.Web;

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Class that encapsulates the http protocol specifics for a SkyDrive request
    /// </summary>
    public class SkyDriveSession : IDisposable
    {
        /// <summary>
        /// The main url for the SkyDrive service, used to query for root folders
        /// </summary>
        private static string SKYDRIVE_URL = "https://docs.live.net/SkyDocsService.svc";

        /// <summary>
        /// The url used to get a the url for authenticating the Passport
        /// </summary>
        private const string PASSPORT_LIST_URL = "https://nexus.passport.com/rdr/pprdr.asp";
        /// <summary>
        /// The name of the http header that contains the login url
        /// </summary>
        private const string PASSPORT_URL_LIST_HEADER = "PassportURLs";
        /// <summary>
        /// A regular expression for extracting the Passport authentication url from the header value
        /// </summary>
        private static readonly Regex PASSPORT_LOGIN_MATCH = new Regex("DALogin=(?<url>[^,]+)");
        /// <summary>
        /// A regular expression for reading the token generation status
        /// </summary>
        private static readonly Regex LOGIN_STATUS_MATCH = new Regex("da-status=(?<status>[^,]+)");
        /// <summary>
        /// A regular expression for reading the actual token result
        /// </summary>
        private static readonly Regex LOGIN_TOKEN_MATCH = new Regex("from-PP=(?<token>[^,]+)");
        /// <summary>
        /// A regular expressing for detecting a Passport nonce string, which can then be re-used with new nonce values
        /// </summary>
        private static readonly Regex NONCE_MATCH = new Regex("Passport1.4[^ ]* ct=(?<nonce>[^,]+).*");

        /// <summary>
        /// A regular expression for identifying a CID as part of a query string
        /// </summary>
        private static readonly Regex CID_FINDER = new Regex("cid=(?<cid>[^&]+)");

        /// <summary>
        /// A template for generating Passport authorization requests
        /// </summary>
        private const string LOGIN_HEADER_TEMPLATE = "Passport1.4 sign-in={0},pwd={1},OrgVerb={2},OrgUrl={3},{4}";
        /// <summary>
        /// A template for generating Passport authorization responses
        /// </summary>
        private const string TOKEN_FORMAT_TEMPLATE = "Passport1.4 from-PP={0}";
        /// <summary>
        /// A template for creating a WLID authorization response
        /// </summary>
        private const string WLID_TOKEN_FORMAT_TEMPLATE = "WLID1.0 {0}";

        /// <summary>
        /// The name of the header that contains the authentication nonce
        /// </summary>
        private const string NONCE_HEADER = "WWW-Authenticate";
        /// <summary>
        /// The name of the redirect header
        /// </summary>
        private const string REDIR_LOCATION_HEADER = "Location";
        /// <summary>
        /// The name of the header that contains authorization information
        /// </summary>
        private const string AUTHORIZATION_HEADER = "Authorization";
        /// <summary>
        /// The name of the header that contains the authorization token
        /// </summary>
        private const string AUTHENTIFICATION_RESULT_HEADER = "Authentication-Info";

        /// <summary>
        /// An xml SOAP request for getting basic account information
        /// </summary>
        private readonly string SOAP_GETACCOUNT_INFO_REQUEST = "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<s:Body><GetWebAccountInfoRequest xmlns=\"http://schemas.microsoft.com/clouddocuments\">" +
                "<BaseRequest xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                "<ClientAppId>" + USER_AGENT + "</ClientAppId><Market>en-US</Market>" +
                "<SkyDocsServiceVersion>v1.0</SkyDocsServiceVersion></BaseRequest>" +
                "<GetReadWriteLibrariesOnly>true</GetReadWriteLibrariesOnly></GetWebAccountInfoRequest></s:Body></s:Envelope>";

        /// <summary>
        /// A template for requesting file creation
        /// </summary>
        private const string CREATE_FILE_TEMPLATE = "<?xml version=\"1.0\" encoding=\"utf-8\"?><entry xmlns:live=\"http://api.live.com/schemas\" xmlns=\"http://www.w3.org/2005/Atom\"><title>{0}</title><live:type>Document</live:type><live:resolveNameConflict>false</live:resolveNameConflict></entry>";
        /// <summary>
        /// A template for requesting subfolder creation
        /// </summary>
        private const string CREATE_SUBFOLDER_TEMPLATE = "<?xml version=\"1.0\" encoding=\"utf-8\"?><entry xmlns:live=\"http://api.live.com/schemas\" xmlns=\"http://www.w3.org/2005/Atom\"><title>{0}</title><live:type>Folder</live:type><live:resolveNameConflict>false</live:resolveNameConflict></entry>";
        /// <summary>
        /// A template for requesting root folder creation
        /// </summary>
        private const string CREATE_ROOTFOLDER_TEMPLATE = "<?xml version=\"1.0\" encoding=\"utf-8\"?><entry xmlns:live=\"http://api.live.com/schemas\" xmlns=\"http://www.w3.org/2005/Atom\"><title>{0}</title><live:type>Library</live:type><live:category>Document</live:category><live:sharingLevel>Private</live:sharingLevel></entry>";

        /// <summary>
        /// The user agent applied to all requests
        /// </summary>
        private readonly static string USER_AGENT = "Duplicati SkyDrive client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();


        /// <summary>
        /// The Passport username
        /// </summary>
        private string m_username;
        /// <summary>
        /// The Passport password
        /// </summary>
        private string m_password;
        /// <summary>
        /// The name of the root folder
        /// </summary>
        private string m_rootfolder;
        /// <summary>
        /// The prefix (aka subfolder) path
        /// </summary>
        private string m_prefix;

        /// <summary>
        /// The url used to authenticate against
        /// </summary>
        private string m_loginUrl;
        /// <summary>
        /// The CID of the folder this session is bound to
        /// </summary>
        private string m_folderCID;
        /// <summary>
        /// The passport token used to authticate WebDAV requests
        /// </summary>
        private string m_mainpassporttoken;
        /// <summary>
        /// The WLID token used to authenticate requests
        /// </summary>
        private string m_mainwlidtoken;
        /// <summary>
        /// The template used to generate nonce values
        /// </summary>
        private string m_noncetemplate;
        /// <summary>
        /// The CID of the user
        /// </summary>
        private string m_userCID;
        /// <summary>
        /// A list of filenames, used to obtain the CID of files
        /// </summary>
        private List<IFileEntry> m_filenameCache;
        /// <summary>
        /// A lookup table with all WEBDAV folders, key is the root foldername, value is the WEBDAV url
        /// </summary>
        private Dictionary<string, string> m_webdav_rootFolderCache = null;

        /// <summary>
        /// A lock used to prevent loading the loginUrl multiple times
        /// </summary>
        private static object _lock = new object();
        /// <summary>
        /// The login url used to authenticate, kept static because it takes a few seconds to read,
        /// and it appears to be completely fixed: https://login.live.com/login2.srf
        /// </summary>
        private static string _loginUrl = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="SkyDriveSession"/> class.
        /// </summary>
        /// <param name="username">The Passport username.</param>
        /// <param name="password">The Passport password.</param>
        /// <param name="rootfolder">The rootfolder.</param>
        /// <param name="prefix">The prefix (aka subfolder path).</param>
        /// <param name="createFolders">if set to <c>true</c> missing folders will be created.</param>
        public SkyDriveSession(string username, string password, string rootfolder, string prefix, bool createFolders)
        {
            //Cache basic info
            m_username = username;
            m_password = password;
            m_rootfolder = rootfolder;
            m_prefix = prefix;

            //The call is sooo slow, and won't change often (if ever).
            //Worst case scenario: the app needs to be restarted
            if (string.IsNullOrEmpty(_loginUrl))
                lock(_lock)
                    if (string.IsNullOrEmpty(_loginUrl))
                        _loginUrl = GetLoginUrl();

            m_loginUrl = _loginUrl;
            //Populate m_userCID, m_noncetemplate and m_webdav_rootFolderCache
            SetupConnection();
            m_folderCID = GetFolderCID(rootfolder, prefix, createFolders);
        }


        /// <summary>
        /// Gets the login URL.
        /// </summary>
        /// <returns>The login url</returns>
        private string GetLoginUrl()
        {
            HttpWebRequest getUrls = (HttpWebRequest)WebRequest.Create(PASSPORT_LIST_URL);
            getUrls.UserAgent = USER_AGENT;
            using (HttpWebResponse resp = (HttpWebResponse)Library.Utility.Utility.SafeGetResponse(getUrls))
            {
                string header = resp.Headers[PASSPORT_URL_LIST_HEADER];
                if (string.IsNullOrEmpty(header))
                    throw new Exception(Strings.SkyDrive.NoPassportLoginUrls);
                string loginurl = PASSPORT_LOGIN_MATCH.Match(header).Groups["url"].Value;

                if (string.IsNullOrEmpty(loginurl))
                    throw new Exception(Strings.SkyDrive.NoPassportLoginUrls);

                if (!loginurl.StartsWith("https://"))
                    loginurl = "https://" + loginurl;

                return loginurl;
            }
        }

        /// <summary>
        /// Reads the user CID, generates the noncetemplate and populates the WEBDAV folder list
        /// </summary>
        public void SetupConnection()
        {
            m_webdav_rootFolderCache = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

            HttpWebRequest listRequest = CreateGetWebAccountInfoRequest();

            //We use the first request to extract the nonce template
            if (string.IsNullOrEmpty(m_noncetemplate))
            {
                using (HttpWebResponse resp = (HttpWebResponse)Library.Utility.Utility.SafeGetResponse(listRequest))
                {
                    string nonce = resp.Headers[NONCE_HEADER];
                    if (string.IsNullOrEmpty(nonce))
                        throw new Exception(Strings.SkyDrive.RequestDidNotRequireAuthentificationError);
                    Match m = NONCE_MATCH.Match(nonce);
                    if (!m.Success)
                        throw new Exception(string.Format(Strings.SkyDrive.InvalidNonceError, nonce));

                    m_noncetemplate =
                        nonce.Substring(0, m.Groups["nonce"].Index) +
                        "{0}" +
                        nonce.Substring(m.Groups["nonce"].Index + m.Groups["nonce"].Length);
                }

                listRequest = CreateGetWebAccountInfoRequest();
            }

            //Get the list of root folders, the user CID and the urls for webdav folders
            XmlDocument doc = new XmlDocument();
            using (HttpWebResponse resp = (HttpWebResponse)Library.Utility.Utility.SafeGetResponse(listRequest))
            using (System.IO.Stream s = resp.GetResponseStream())
                doc.Load(s);

            XmlNamespaceManager mgr = new XmlNamespaceManager(doc.NameTable);
            mgr.AddNamespace("ms", "http://schemas.microsoft.com/clouddocuments");

            if (string.IsNullOrEmpty(m_userCID))
            {
                //Extract the CID from the NewLibraryUrl node
                XmlNode cidnode = doc.SelectSingleNode("//ms:NewLibraryUrl", mgr);
                if (cidnode == null)
                    throw new Exception(Strings.SkyDrive.UnableToFindCIDError);
                m_userCID = CID_FINDER.Match(cidnode.InnerText).Groups["cid"].Value;
                if (string.IsNullOrEmpty(m_userCID))
                    throw new Exception(Strings.SkyDrive.UnableToFindCIDError);
            }

            //Load the list of WEBDAV urls
            foreach (XmlNode n in doc.SelectNodes("//ms:Library", mgr))
                m_webdav_rootFolderCache.Add(n.SelectSingleNode("ms:DisplayName", mgr).InnerText, n.SelectSingleNode("ms:DavUrl", mgr).InnerText);
        }

        /// <summary>
        /// Gets the folder CID for the folder this session is bound to.
        /// </summary>
        /// <param name="rootfolder">The rootfolder.</param>
        /// <param name="prefix">The prefix.</param>
        /// <param name="createFolder">if set to <c>true</c> missing folders will be created.</param>
        /// <returns>The folder CID</returns>
        private string GetFolderCID(string rootfolder, string prefix, bool createFolder)
        {
            //Obtain a valid passport token for downloading with WebDAV
            m_mainpassporttoken = GetAuthenticationToken("GET", RootUrl);

            //Assign the WLID1.0 token, which is the same as the Passport1.4 token, but with another prefix
            m_mainwlidtoken = ConvertPassportTokenToWLIDToken(m_mainpassporttoken);

            string[] folders = (rootfolder + prefix).Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            TaggedFileEntry curfolder = null;

            //Do a recursive lookup on the folders, which is required to get the CID for the folder
            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder))
                    continue;

                TaggedFileEntry newfolder = null;
                foreach (TaggedFileEntry tfe in ListFolderItems(curfolder == null ? null : curfolder.CID))
                    if (tfe.Name.Equals(folder, StringComparison.InvariantCultureIgnoreCase))
                    {
                        newfolder = tfe;
                        break;
                    }

                if (newfolder == null && createFolder)
                {
                    //Create the folder and re-list the contents
                    CreateFolder(folder, curfolder == null ? null : curfolder.CID);
                    foreach (TaggedFileEntry tfe in ListFolderItems(curfolder == null ? null : curfolder.CID))
                        if (tfe.Name.Equals(folder, StringComparison.InvariantCultureIgnoreCase))
                        {
                            newfolder = tfe;
                            break;
                        }
                }

                //If the folder was not created, inform the caller
                if (newfolder == null)
                    throw new Interface.FolderMissingException();

                curfolder = newfolder;
            }

            if (curfolder == null)
                throw new FolderMissingException();

            return curfolder.CID;

        }

        /// <summary>
        /// Creates a WebAccountInfo request used to read basic account information.
        /// </summary>
        /// <returns>A configured request</returns>
        private HttpWebRequest CreateGetWebAccountInfoRequest()
        {
            HttpWebRequest soapRequest = (HttpWebRequest)WebRequest.Create(SKYDRIVE_URL);
            soapRequest.AllowAutoRedirect = false;
            soapRequest.PreAuthenticate = false;
            soapRequest.UserAgent = USER_AGENT;
            soapRequest.Headers["SOAPAction"] = "GetWebAccountInfo";
            soapRequest.Headers["translate"] = "f";
            soapRequest.Method = "POST";
            soapRequest.ContentType = "text/xml; charset=utf-8";
            SetAuthenticationToken(soapRequest);

            byte[] data = System.Text.Encoding.UTF8.GetBytes(SOAP_GETACCOUNT_INFO_REQUEST);
            using (System.IO.Stream s = Utility.Utility.SafeGetRequestStream(soapRequest))
                s.Write(data, 0, data.Length);

            return soapRequest;
        }

        /// <summary>
        /// Gets the root URL onto which WLID requests can be made.
        /// </summary>
        /// <value>The root URL.</value>
        public string RootUrl
        {
            get
            {
                return String.Format(
                    "http://cid-{0}.users.api.live.net/Users({1})/Files",
                    m_userCID,
                    Int64.Parse(m_userCID, System.Globalization.NumberStyles.HexNumber));
            }
        }

        /// <summary>
        /// Gets the folder CID for the folder this session is bound to.
        /// </summary>
        /// <value>The folder CID.</value>
        public string FolderCID
        {
            get { return m_folderCID; }
        }

        /// <summary>
        /// Gets the URL for the folder this session is bound to.
        /// </summary>
        /// <value>The folder URL.</value>
        public string FolderUrl
        {
            get { return GetFolderUrl(m_folderCID); }
        }

        /// <summary>
        /// Gets the URL for a specific folder.
        /// </summary>
        /// <param name="folderCID">The folder CID.</param>
        /// <returns></returns>
        private string GetFolderUrl(string folderCID)
        {
            return String.Format("{0}/Folders('{1}')", RootUrl, folderCID);
        }

        /// <summary>
        /// Gets the URL for a specific file.
        /// </summary>
        /// <param name="fileCID">The file CID.</param>
        /// <returns></returns>
        private string GetFileUrl(string fileCID)
        {
            return String.Format("{0}/Files('{1}')", RootUrl, fileCID);
        }


        /// <summary>
        /// Creates a nonce using the template.
        /// </summary>
        /// <param name="verb">The http verb.</param>
        /// <param name="url">The http request URL.</param>
        /// <returns>The generated nonce</returns>
        private string CreateNonce(string verb, string url)
        {
            //The nonce template seems to be statically defined as:
            //"Passport1.4 ct={0},rver=6.1.6206.0,wp=MBI,lc=1033,id=250206"
            // but just to be sure, we pick it up from the server
            return String.Format(m_noncetemplate, (int)Math.Floor((DateTime.Now - Library.Utility.Utility.EPOCH).TotalSeconds));
        }

        /// <summary>
        /// Sets the WLID authentication token on the request.
        /// </summary>
        /// <param name="req">The req to assign the WLID token to</param>
        private void SetWlidAuthenticationToken(HttpWebRequest req)
        {
            req.Headers[AUTHORIZATION_HEADER] = m_mainwlidtoken;
            //Hmm, magic header value, without it all requests are denied
            req.Headers["AppId"] = "1140851978";
            req.UserAgent = USER_AGENT;
        }

        /// <summary>
        /// Generates and sets the Passport authentication token on the request.
        /// </summary>
        /// <param name="req">The request to set the token on.</param>
        private void SetAuthenticationToken(HttpWebRequest req)
        {
            string s = GetAuthenticationToken(req.Method, req.RequestUri.ToString());
            if (!string.IsNullOrEmpty(s))
                req.Headers[AUTHORIZATION_HEADER] = s;
            req.UserAgent = USER_AGENT;
        }

        /// <summary>
        /// Gets a Passport authentication token for a verb and url.
        /// </summary>
        /// <param name="verb">The http verb.</param>
        /// <param name="url">The http request URL.</param>
        /// <returns></returns>
        private string GetAuthenticationToken(string verb, string url)
        {
            if (string.IsNullOrEmpty(m_noncetemplate))
                return null;

            try
            {
                //Get a authentification token
                HttpWebRequest getToken = (HttpWebRequest)WebRequest.Create(m_loginUrl);
                getToken.UserAgent = USER_AGENT;
                getToken.Headers[AUTHORIZATION_HEADER] = string.Format(LOGIN_HEADER_TEMPLATE, HttpUtility.UrlEncode(m_username), HttpUtility.UrlEncode(m_password), verb, url, CreateNonce(verb, url));

                using (HttpWebResponse resp = (HttpWebResponse)Library.Utility.Utility.SafeGetResponse(getToken))
                {
                    string token_header = resp.Headers[AUTHENTIFICATION_RESULT_HEADER];
                    string redir = resp.Headers[REDIR_LOCATION_HEADER];

                    string status = LOGIN_STATUS_MATCH.Match(token_header).Groups["status"].Value;
                    string token = LOGIN_TOKEN_MATCH.Match(token_header).Groups["token"].Value;

                    if (status != "success")
                        throw new Exception(string.Format(Strings.SkyDrive.LoginFailedError, status));
                    if (string.IsNullOrEmpty(token))
                        throw new Exception(Strings.SkyDrive.NoTokenError);

                    return string.Format(TOKEN_FORMAT_TEMPLATE, token);
                }
            }
            catch (WebException wex)
            {
                if (wex.Response is HttpWebResponse && ((HttpWebResponse)wex.Response).StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (IsPasswordTooLong(m_password))
                        throw new Exception(Strings.SkyDriveSession.InvalidPasswordLongPasswordFound, wex);
                    else if (HasInvalidChars(m_password))
                        throw new Exception(Strings.SkyDriveSession.InvalidPasswordInvalidCharsFound, wex);
                    else
                        throw new Exception(Strings.SkyDriveSession.InvalidUsernameOrPassword, wex);
                }

                throw;
            }
        }

        /// <summary>
        /// Converts the Passport token to a WLID token.
        /// </summary>
        /// <param name="passporttoken">The passport token.</param>
        /// <returns>The WLID token</returns>
        private static string ConvertPassportTokenToWLIDToken(string passporttoken)
        {
            string basetoken = LOGIN_TOKEN_MATCH.Match(passporttoken).Groups["token"].Value;
            if (basetoken.StartsWith("'") && basetoken.EndsWith("'"))
                basetoken = basetoken.Substring(1, basetoken.Length - 2);
            return string.Format(WLID_TOKEN_FORMAT_TEMPLATE, basetoken);
        }

        /// <summary>
        /// Creates a new folder.
        /// </summary>
        /// <param name="name">The name of the folder to create.</param>
        /// <param name="parentCid">The parent cid. If this is null, a new root folder will be created.</param>
        public void CreateFolder(string name, string parentCid)
        {
            HttpWebRequest req;
            if (string.IsNullOrEmpty(parentCid))
                req = (HttpWebRequest)WebRequest.Create(RootUrl);
            else
                req = (HttpWebRequest)WebRequest.Create(GetFolderUrl(parentCid));
            req.Method = WebRequestMethods.Http.Post;
            req.Accept = "application/atom+xml";
            req.ContentType = req.Accept;
            
            SetWlidAuthenticationToken(req);

            byte[] data;
            if (string.IsNullOrEmpty(parentCid))
                data = System.Text.Encoding.UTF8.GetBytes(string.Format(CREATE_ROOTFOLDER_TEMPLATE, name));
            else
                data = System.Text.Encoding.UTF8.GetBytes(string.Format(CREATE_SUBFOLDER_TEMPLATE, name));

            req.ContentLength = data.Length;

            using (System.IO.Stream s = Utility.Utility.SafeGetRequestStream(req))
                s.Write(data, 0, data.Length);

            using (HttpWebResponse resp = (HttpWebResponse)Library.Utility.Utility.SafeGetResponse(req))
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                    throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
            }

            //If we added a root folder, update the webdab list
            if (string.IsNullOrEmpty(parentCid))
                SetupConnection();
        }

        private TaggedFileEntry ParseXmlAtomEntry(XmlNode node, XmlNamespaceManager mgr)
        {
            if (mgr == null)
                mgr = new XmlNamespaceManager(node.OwnerDocument.NameTable);

            if (!mgr.HasNamespace("live"))
                mgr.AddNamespace("live", "http://api.live.com/schemas");
            if (!mgr.HasNamespace("atom"))
                mgr.AddNamespace("atom", "http://www.w3.org/2005/Atom");

            XmlNode title = node.SelectSingleNode("atom:title", mgr);
            XmlNode id = node.SelectSingleNode("live:resourceId", mgr);
            XmlNode url = node.SelectSingleNode("atom:id", mgr);

            if (title == null || id == null || url == null)
                return null;

            XmlNode updated = node.SelectSingleNode("atom:updated", mgr);
            XmlNode size = node.SelectSingleNode("live:size", mgr);
            XmlNode type = node.SelectSingleNode("live:type", mgr);

            string u = url.InnerText;
            XmlNode content = node.SelectSingleNode("atom:content", mgr);
            if (content != null) content = content.Attributes["src"];
            if (content != null) u = content.Value;

            string editLink = null;
            string altLink = null;

            foreach (XmlNode n in node.SelectNodes("atom:link", mgr))
                if (n.Attributes["rel"] != null && n.Attributes["href"] != null)
                {
                    if (n.Attributes["rel"].Value == "edit-media")
                        editLink = n.Attributes["href"].Value;
                    else if (n.Attributes["rel"].Value == "alternate")
                        altLink = n.Attributes["href"].Value;
                }

            TaggedFileEntry tf = new TaggedFileEntry(title.InnerText, u, id.InnerText, altLink, editLink);
            try { tf.LastAccess = tf.LastModification = DateTime.Parse(updated.InnerText); }
            catch { }

            try { tf.Size = long.Parse(size.InnerText); }
            catch { }

            try { tf.IsFolder = type.InnerText.Equals("Library", StringComparison.InvariantCultureIgnoreCase) || type.InnerText.Equals("Folder", StringComparison.InvariantCultureIgnoreCase); }
            catch { }

            return tf;
        }

        private List<IFileEntry> ParseXmlAtomDocument(XmlDocument doc)
        {
            List<IFileEntry> results = new List<IFileEntry>();
            XmlNamespaceManager mgr = new XmlNamespaceManager(doc.NameTable);
            mgr.AddNamespace("live", "http://api.live.com/schemas");
            mgr.AddNamespace("atom", "http://www.w3.org/2005/Atom");

            foreach (XmlNode n in doc.SelectNodes("//atom:entry", mgr))
            {
                TaggedFileEntry tf = ParseXmlAtomEntry(n, mgr);
                if (tf != null)
                    results.Add(tf);
            }

            return results;
        }

        /// <summary>
        /// Lists the items in a specific folder.
        /// </summary>
        /// <param name="parentCid">The parent folder CID. If this is null, the root folder items are listed</param>
        /// <returns>A list of elements in the folder</returns>
        public List<IFileEntry> ListFolderItems(string parentCid)
        {
            HttpWebRequest req;
            if (string.IsNullOrEmpty(parentCid))
                req = (HttpWebRequest)WebRequest.Create(RootUrl);
            else
                req = (HttpWebRequest)WebRequest.Create(GetFolderUrl(parentCid));

            req.Accept = "application/atom+xml";
            req.ContentType = req.Accept;
            req.ContentLength = 0;
            SetWlidAuthenticationToken(req);

            XmlDocument doc = new XmlDocument();
            using (HttpWebResponse resp = (HttpWebResponse)Library.Utility.Utility.SafeGetResponse(req))
            using (System.IO.Stream s = resp.GetResponseStream())
                doc.Load(s);

            List<IFileEntry> results = ParseXmlAtomDocument(doc);

            //Cache the results if we listed the folder that the session is bound to
            if (m_folderCID != null && m_folderCID.Equals(parentCid))
                m_filenameCache = new List<IFileEntry>(results);

            return results;

        }

        /// <summary>
        /// Returns a WEBDAV url for a given file
        /// </summary>
        /// <param name="remotename">The name of the file to generate an URL for</param>
        /// <returns>The WEBDAV URL for the file</returns>
        private string GetWebDavUrl(string remotename)
        {
            if (!m_webdav_rootFolderCache.ContainsKey(m_rootfolder))
                throw new FolderMissingException();

            return m_webdav_rootFolderCache[m_rootfolder] + m_prefix + remotename;
        }

        /// <summary>
        /// Looks for a file and returns the file information, including the CID
        /// </summary>
        /// <param name="name">The name of the file to look for</param>
        /// <returns>The file information or null</returns>
        private TaggedFileEntry GetFileInfoFromCache(string name)
        {
            if (m_filenameCache == null)
                return GetFileInfo(name);

            foreach (TaggedFileEntry f in m_filenameCache)
            {
                if (f.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    return f;
            }

            return null;

        }

        /// <summary>
        /// Looks for a file and returns the file information, including the CID
        /// </summary>
        /// <param name="name">The name of the file to look for</param>
        /// <returns>The file information or null</returns>
        private TaggedFileEntry GetFileInfo(string name)
        {
            //Look in the cache first
            if (m_filenameCache != null)
                foreach (TaggedFileEntry f in m_filenameCache)
                {
                    if (f.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                        return f;
                }

            //No match, perform a new list request
            foreach (TaggedFileEntry f in ListFolderItems(m_folderCID))
            {
                if (f.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    return f;
            }

            return null;
        }

        /// <summary>
        /// Deletes a file.
        /// </summary>
        /// <param name="remotename">The remote file name.</param>
        /// <returns>The server response</returns>
        public void DeleteFile(string remotename)
        {
            try
            {
                TaggedFileEntry fileinfo = GetFileInfo(remotename);
                if (fileinfo == null)
                    throw new System.IO.FileNotFoundException(); //TODO: Should throw 404?

                //First we need the content url
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(GetFileUrl(fileinfo.CID));
                req.AllowAutoRedirect = false;
                req.Method = "DELETE";
                req.KeepAlive = false;
                //Delete uses the default timeout
                //req.Timeout = System.Threading.Timeout.Infinite;
                SetWlidAuthenticationToken(req);

                using (HttpWebResponse resp = (HttpWebResponse)Library.Utility.Utility.SafeGetResponse(req))
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
                    m_filenameCache.Remove(fileinfo);
                }
            }
            catch
            {
                m_filenameCache = null;
                throw;
            }
        }

        /// <summary>
        /// Uploads a file.
        /// </summary>
        /// <param name="remotename">The remote file name.</param>
        /// <param name="data">The data that represents the file.</param>
        /// <returns>The server response</returns>
        public void UploadFile(string remotename, System.IO.Stream data)
        {
            TaggedFileEntry fileinfo = null;

            //This is done slightly odd, because we do not want to
            // re-issue the file listing if it can be avoided
            if (m_filenameCache == null)
                fileinfo = GetFileInfoFromCache(remotename);
            else
            {
                foreach (TaggedFileEntry f in m_filenameCache)
                    if (f.Name.Equals(remotename, StringComparison.InvariantCultureIgnoreCase))
                    {
                        fileinfo = f;
                        break;
                    }
            }

            if (fileinfo != null && fileinfo.EditUrl == null)
            {
                try { this.DeleteFile(remotename); }
                catch { }

                fileinfo = GetFileInfo(remotename);
                if (fileinfo != null && fileinfo.EditUrl == null)
                    throw new Exception(string.Format(Strings.SkyDrive.FileIsReadOnlyError, remotename));
            }

            HttpWebRequest req;
            if (fileinfo == null)
            {
                try
                {
                    string boundary = String.Format("---------------------------{0:x}", DateTime.Now.Ticks);

                    req = (HttpWebRequest)WebRequest.Create(FolderUrl);

                    req.Method = WebRequestMethods.Http.Post;
                    req.ContentType = "multipart/related; type=\"application/atom+xml\"; boundary=\"" + boundary + "\"";
                    req.Accept = "application/atom+xml";
                    req.AllowAutoRedirect = false;
                    req.KeepAlive = false;

                    //We only depend on the ReadWriteTimeout
                    req.Timeout = System.Threading.Timeout.Infinite;
                    SetWlidAuthenticationToken(req);

                    byte[] createRequest = System.Text.Encoding.UTF8.GetBytes(string.Format(CREATE_FILE_TEMPLATE, HttpUtility.HtmlEncode(remotename)));

                    byte[] newLine = Encoding.UTF8.GetBytes("\r\n");
                    byte[] lastBoundary = Encoding.UTF8.GetBytes("--" + boundary + "--\r\n");


                    byte[] xmlHeader = System.Text.Encoding.UTF8.GetBytes(
                        "--" + boundary + "\r\n" +
                        "MIME-Version: 1.0" + "\r\n" +
                        "Content-Type: application/atom+xml" + "\r\n\r\n");

                    byte[] dataHeader = System.Text.Encoding.UTF8.GetBytes(
                        "--" + boundary + "\r\n" +
                        "MIME-Version: 1.0" + "\r\n" +
                        "Content-Type: application/octet-stream" + "\r\n\r\n");


                    try { req.ContentLength = xmlHeader.Length + createRequest.Length + newLine.Length + dataHeader.Length + data.Length + newLine.Length + lastBoundary.Length; }
                    catch { }

                    using (System.IO.Stream s = Utility.Utility.SafeGetRequestStream(req))
                    {
                        s.Write(xmlHeader, 0, xmlHeader.Length);
                        s.Write(createRequest, 0, createRequest.Length);
                        s.Write(newLine, 0, newLine.Length);
                        s.Write(dataHeader, 0, dataHeader.Length);
                        Utility.Utility.CopyStream(data, s);
                        s.Write(newLine, 0, newLine.Length);
                        s.Write(lastBoundary, 0, lastBoundary.Length);
                    }

                    using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)Library.Utility.Utility.SafeGetResponse(req))
                    {
                        int code = (int)resp.StatusCode;
                        if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                            throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                        //If all goes well, we should now get an atom entry describing the new element
                        System.Xml.XmlDocument xml = new XmlDocument();
                        using (System.IO.Stream s = resp.GetResponseStream())
                            xml.Load(s);

                        TaggedFileEntry tf = ParseXmlAtomEntry(xml["entry"], null);
                        if (tf == null)
                            throw new Exception(string.Format(Strings.SkyDrive.UploadFailedError, remotename));

                        if (fileinfo != null)
                            m_filenameCache.Remove(fileinfo);
                        m_filenameCache.Add(tf);
                    }
                }
                catch
                {
                    m_filenameCache = null;
                    throw;
                }
            }
            else
            {
                //Overwrite is simpler
                req = (HttpWebRequest)WebRequest.Create(fileinfo.EditUrl);
                req.Method = WebRequestMethods.Http.Put;
                req.ContentLength = data.Length;
                req.ContentType = "application/octet-stream";
                req.AllowAutoRedirect = false;
                req.KeepAlive = false;

                //We only depend on the ReadWriteTimeout
                req.Timeout = System.Threading.Timeout.Infinite;
                SetWlidAuthenticationToken(req);

                using (System.IO.Stream s = Utility.Utility.SafeGetRequestStream(req))
                    Utility.Utility.CopyStream(data, s);

                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)Library.Utility.Utility.SafeGetResponse(req))
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
                }
            }
        }

        /// <summary>
        /// Downloads a file.
        /// </summary>
        /// <param name="remotename">The remote file name.</param>
        /// <returns>The server response</returns>
        public HttpWebResponse DownloadFile(string remotename)
        {
            TaggedFileEntry fileinfo = GetFileInfo(remotename);
            if (fileinfo == null)
                throw new System.IO.FileNotFoundException(); //TODO: Should throw 404?

            //This method is much preferred because it allows us to use non-url-friendly characters
            //For some reason it will not accept the WLID token :(
            //I also tried creating WLID and passport tokens for the URL and
            // the hostnames, but everything redirects.
            //The browser redirects to an auth page that sets a WLSRDAuth
            // cookie, but I cannot find any information on that

            /*HttpWebRequest req = (HttpWebRequest)WebRequest.Create(fileinfo.Url);
            req.Method = WebRequestMethods.Http.Get;
            req.AllowAutoRedirect = false;
            req.KeepAlive = false;
            //We only depend on the ReadWriteTimeout
            req.Timeout = System.Threading.Timeout.Infinite;
            SetWlidAuthenticationToken(req);
            */

            //This uses a WebDAV hack to download files via WebDAV,
            //I have not yet figured out how to encode special characters,
            // but it works with -_. and that should be enough for Duplicati
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(GetWebDavUrl(remotename));
            req.Method = WebRequestMethods.Http.Get;
            req.AllowAutoRedirect = false;
            req.KeepAlive = false;
            //We only depend on the ReadWriteTimeout
            req.Timeout = System.Threading.Timeout.Infinite;
            req.UserAgent = USER_AGENT;
            req.Headers[AUTHORIZATION_HEADER] = m_mainpassporttoken;

            HttpWebResponse resp = (HttpWebResponse)Library.Utility.Utility.SafeGetResponse(req);
            int code = (int)resp.StatusCode;

            //For some reason Mono does not throw this automatically
            if (code < 200 || code >= 300)
            {
                //if (resp.StatusCode == HttpStatusCode.Moved || resp.StatusCode == HttpStatusCode.MovedPermanently)
                using (resp)
                    throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
            }

            return resp;
        }


        //For some reason this always throws 403, perhaps some locking is required?
        /*private HttpWebResponse Upload_webdav(string remotename, System.IO.Stream data)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(GetWebDavUrl(remotename));
            req.AllowAutoRedirect = false;
            req.Method = "PUT";
            req.ContentType = "application/octet-stream";
            req.ContentLength = data.Length;
            //We only depend on the ReadWriteTimeout
            req.Timeout = System.Threading.Timeout.Infinite;
            SetAuthenticationToken(req);

            using (System.IO.Stream s = Utility.Utility.SafeGetRequestStream(req))
                Utility.Utility.CopyStream(data, s);

            return (System.Net.HttpWebResponse)Library.Utility.Utility.SafeGetResponse(req);

        }*/

        #region IDisposable Members

        public void Dispose()
        {
            m_username = null;
            m_password = null;
            m_rootfolder = null;
            m_prefix = null;
            m_folderCID = null;
            m_mainwlidtoken = null;
            m_noncetemplate = null;
            m_userCID = null;
            m_filenameCache = null;
            m_webdav_rootFolderCache = null;
        }

        #endregion


        /// <summary>
        /// Utility function to test if password is so long that it can cause problems
        /// </summary>
        /// <param name="password">The password to validate</param>
        /// <returns>True if the password is too long, false otherwise</returns>
        public static bool IsPasswordTooLong(string password)
        {
            if (string.IsNullOrEmpty(password))
                return false;

            return password.Length > 16;
        }

        /// <summary>
        /// Utility function to test if the password contains non-standard characters
        /// </summary>
        /// <param name="password">The password to validate</param>
        /// <returns>True if the password has invalid chars, false otherwise</returns>
        public static bool HasInvalidChars(string password)
        {
            bool invalidChars = false;
            foreach (char c in password)
                invalidChars |= !("!&()[~@".IndexOf(c) >= 0 || char.IsLetterOrDigit(c));

            return invalidChars;
        }
    }
}
