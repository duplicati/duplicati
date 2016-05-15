﻿#region Disclaimer / License
// Copyright (C) 2016, The Duplicati Team
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
using System.IO;
using System.Linq;
using System.Text;

using Duplicati.Library.Interface;

using SP = Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client; // Plain 'using' for extension methods

namespace Duplicati.Library.Backend
{

    /// <summary>
    /// Class implementing Duplicati's backend for SharePoint.
    /// </summary>
    /// <remarks>
    /// Find SharePoint Server 2013 Client Components SDK (15.xxx): https://www.microsoft.com/en-us/download/details.aspx?id=35585
    /// Currently used is MS-package from nuget (for SharePoint Server 2016, 16.xxx): https://www.nuget.org/packages/Microsoft.SharePointOnline.CSOM
    /// Infos for further development (pursue on demand):
    /// Using ADAL-Tokens (Azure Active directory): https://samlman.wordpress.com/2015/02/27/using-adal-access-tokens-with-o365-rest-apis-and-csom/
    /// Outline:
    /// - On AzureAD --> AddNewApp and configure access to O_365 (SharePoint/OneDrive4Busi)
    ///              --> Get App's CLIENT ID and REDIRECT URIS.
    /// - Get SAML token (WebRequest).
    /// - Add auth code with access token in event ctx.ExecutingWebRequest
    ///   --> e.WebRequestExecutor.RequestHeaders[“Authorization”] = “Bearer ” + ar.AccessToken;
    /// </remarks>
    public class SharePointBackend : IBackend, IStreamingBackend
    {

        #region [Variables and constants declarations]

        /// <summary> Auth-stripped HTTPS-URI as passed to constructor. </summary>
        private Utility.Uri m_orgUrl;
        /// <summary> Server relative path to backup folder. </summary>
        private string m_serverRelPath;
        /// <summary> User's credentials to create client context </summary>
        private System.Net.ICredentials m_userInfo;
        /// <summary> Flag indicating to move files to recycler on deletion. </summary>
        private bool m_deleteToRecycler = false;

        /// <summary> URL to SharePoint web. Will be determined from m_orgUri on first use. </summary>
        private string m_spWebUrl;
        /// <summary> Current context to SharePoint web. </summary>
        private SP.ClientContext m_spContext;

        #endregion

        #region [Public Properties]

        public virtual string ProtocolKey
        {
            get { return "mssp"; }
        }

        public virtual string DisplayName
        {
            get { return Strings.SharePoint.DisplayName; }
        }

        public virtual string Description
        {
            get { return Strings.SharePoint.Description; }
        }

        public virtual bool SupportsStreaming
        {
            get { return true; }
        }

        public virtual IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.SharePoint.DescriptionAuthPasswordShort, Strings.SharePoint.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.SharePoint.DescriptionAuthUsernameShort, Strings.SharePoint.DescriptionAuthUsernameLong),
                    new CommandLineArgument("integrated-authentication", CommandLineArgument.ArgumentType.Boolean, Strings.SharePoint.DescriptionIntegratedAuthenticationShort, Strings.SharePoint.DescriptionIntegratedAuthenticationLong),
                    new CommandLineArgument("delete-to-recycler", CommandLineArgument.ArgumentType.Boolean, Strings.SharePoint.DescriptionUseRecyclerShort, Strings.SharePoint.DescriptionUseRecyclerLong),
                });
            }
        }

        #endregion

        #region [Constructors]

        public SharePointBackend()
        { }

        public SharePointBackend(string url, Dictionary<string, string> options)
        {
            m_deleteToRecycler = Utility.Utility.ParseBoolOption(options, "delete-to-recycler");

            var u = new Utility.Uri(url);
            u.RequireHost();

            // Create sanitized plain https-URI (note: still has double slashes for processing web)
            m_orgUrl = new Utility.Uri("https", u.Host, u.Path, null, null, null, u.Port);

            // Actual path to Web will be searched for on first use. Ctor should not throw.
            m_spWebUrl = null;

            m_serverRelPath = u.Path;
            if (!m_serverRelPath.StartsWith("/"))
                m_serverRelPath = "/" + m_serverRelPath;
            if (!m_serverRelPath.EndsWith("/"))
                m_serverRelPath += "/";
            // remove marker for SP-Web
            m_serverRelPath = m_serverRelPath.Replace("//", "/");

            // Authentication settings processing:
            // Default: try integrated auth (will normally not work for Office365, but maybe with on-prem SharePoint...).
            // Otherwise: Use settings from URL(precedence) or from command line options.
            bool useIntegratedAuthentication = Utility.Utility.ParseBoolOption(options, "integrated-authentication");

            string useUsername = null;
            string usePassword = null;

            if (!useIntegratedAuthentication)
            {
                if (!string.IsNullOrEmpty(u.Username))
                {
                    useUsername = u.Username;
                    if (!string.IsNullOrEmpty(u.Password))
                        usePassword = u.Password;
                    else if (options.ContainsKey("auth-password"))
                        usePassword = options["auth-password"];
                }
                else
                {
                    if (options.ContainsKey("auth-username"))
                    {
                        useUsername = options["auth-username"];
                        if (options.ContainsKey("auth-password"))
                            usePassword = options["auth-password"];
                    }
                }
            }

            if (useIntegratedAuthentication || (useUsername == null || usePassword == null))
            {
                // This might or might not work for on-premises SP. Maybe support if someone complains...
                m_userInfo = System.Net.CredentialCache.DefaultNetworkCredentials;
            }
            else
            {
                System.Security.SecureString securePwd = new System.Security.SecureString();
                usePassword.ToList().ForEach(c => securePwd.AppendChar(c));
                m_userInfo = new Microsoft.SharePoint.Client.SharePointOnlineCredentials(useUsername, securePwd);
                // Other options (also ADAL, see class remarks) might be supported on request.
                // Maybe go in deep then and also look at:
                // - Microsoft.SharePoint.Client.AppPrincipalCredential.CreateFromKeyGroup()
                // - ctx.AuthenticationMode = SP.ClientAuthenticationMode.FormsAuthentication;
                // - ctx.FormsAuthenticationLoginInfo = new SP.FormsAuthenticationLoginInfo(user, pwd);
            }

        }


        #endregion

        #region [Private helper methods]

        /// <summary>
        /// Tries a simple query to test the passed context.
        /// Returns 0 on success, negative if completely invalid, positive if SharePoint error (wrong creds are negative).
        /// </summary>
        private static int testContextForWeb(SP.ClientContext ctx, bool rethrow = true)
        {
            try
            {
                ctx.Load(ctx.Web, w => w.Title);
                ctx.ExecuteQuery(); // should fail and throw if anything wrong.
                string webTitle = ctx.Web.Title;
                if (webTitle == null)
                    throw new UnauthorizedAccessException(Strings.SharePoint.WebTitleReadFailedError);
                return 0;
            }
            catch (Microsoft.SharePoint.Client.ServerException)
            {
                if (rethrow) throw;
                else return 1;
            }
            catch (Exception)
            {
                if (rethrow) throw;
                else return -1;
            }
        }

        /// <summary>
        /// Builds a client context and tries a simple query to test if there's a web.
        /// Returns 0 on success, negative if completely invalid, positive if SharePoint error (likely wrong creds).
        /// </summary>
        private static int testUrlForWeb(string url, System.Net.ICredentials userInfo, bool rethrow, out SP.ClientContext retCtx)
        {
            int result = -1;
            retCtx = null;
            SP.ClientContext ctx = new ClientContext(url);
            try
            {
                ctx.Credentials = userInfo;
                result = testContextForWeb(ctx, rethrow);
                if (result >= 0)
                {
                    retCtx = ctx;
                    ctx = null;
                }
            }
            finally { if (ctx != null) { ctx.Dispose(); } }

            return result;
        }

        /// <summary>
        /// SharePoint has nested subwebs but sometimes different webs
        /// are hooked into a sub path.
        /// For finding files SharePoint is picky to use the correct
        /// path to the web, so we will trial and error here.
        /// The user can give us a hint by supplying an URI with a double
        /// slash to separate web.
        /// Otherwise it's a good guess to look for "/documents", as we expect
        /// that the default document library is used in the path.
        /// If that won't help, we will try all possible pathes from longest
        /// to shortest...
        /// </summary>
        private static string findCorrectWebPath(Utility.Uri orgUrl, System.Net.ICredentials userInfo, out SP.ClientContext retCtx)
        {
            retCtx = null;

            string path = orgUrl.Path;
            int webIndicatorPos = path.IndexOf("//");

            // if a hint is supplied, we will of course use this first.
            if (webIndicatorPos >= 0)
            {
                string testUrl = new Utility.Uri(orgUrl.Scheme, orgUrl.Host, path.Substring(0, webIndicatorPos), null, null, null, orgUrl.Port).ToString();
                if (testUrlForWeb(testUrl, userInfo, false, out retCtx) >= 0)
                    return testUrl;
            }

            // Now go through path and see where we land a success.
            string[] pathParts = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            // first we look for the doc library
            int docLibrary = Array.FindIndex(pathParts, p => StringComparer.InvariantCultureIgnoreCase.Equals(p, "documents"));
            if (docLibrary >= 0)
            {
                string testUrl = new Utility.Uri(orgUrl.Scheme, orgUrl.Host,
                    string.Join("/", pathParts, 0, docLibrary),
                    null, null, null, orgUrl.Port).ToString();
                if (testUrlForWeb(testUrl, userInfo, false, out retCtx) >= 0)
                    return testUrl;
            }

            // last but not least: try one after the other.
            for (int pi = pathParts.Length - 1; pi >= 0; pi--)
            {
                if (pi == docLibrary) continue; // already tested

                string testUrl = new Utility.Uri(orgUrl.Scheme, orgUrl.Host,
                    string.Join("/", pathParts, 0, pi),
                    null, null, null, orgUrl.Port).ToString();
                if (testUrlForWeb(testUrl, userInfo, false, out retCtx) >= 0)
                    return testUrl;
            }

            // nothing worked :(
            return null;
        }

        /// <summary> Return the preconfigured SP.ClientContext to use. </summary>
        private SP.ClientContext getSpClientContext(bool forceNewContext = false)
        {
            if (forceNewContext)
            {
                if (m_spContext != null) m_spContext.Dispose();
                m_spContext = null;
            }

            if (m_spContext == null)
            {
                if (m_spWebUrl == null)
                {
                    m_spWebUrl = findCorrectWebPath(m_orgUrl, m_userInfo, out m_spContext);
                    if (m_spWebUrl == null)
                        throw new System.Net.WebException(Strings.SharePoint.NoSharePointWebFoundError(m_orgUrl.ToString()));
                }
                else
                {
                    // would query: testUrlForWeb(m_spWebUrl, userInfo, true, out m_spContext);
                    m_spContext = new ClientContext(m_spWebUrl);
                    m_spContext.Credentials = m_userInfo;
                }
            }
            return m_spContext;
        }


        /// <summary>
        /// Dedicated code to wrap ExecuteQuery on file ops and check for errors.
        /// We have to check for the exceptions thrown to know about file /folder existence.
        /// Why the funny guys at MS provided an .Exists field stays a mystery...
        /// </summary>
        /// <param name="fileNameInfo"> the filename  </param>
        private void wrappedExecuteQueryOnConext(SP.ClientContext ctx, string serverRelPathInfo, bool isFolder)
        {
            try { ctx.ExecuteQuery(); }
            catch (ServerException ex)
            {
                // funny: If a folder is not found, we still get a FileNotFoundException from Server...?!?
                // Thus, we help ourselves by just passing the info if we wanted to query a folder.
                if (ex.ServerErrorTypeName == "System.IO.DirectoryNotFoundException"
                    || (ex.ServerErrorTypeName == "System.IO.FileNotFoundException" && isFolder))
                    throw new Interface.FolderMissingException(Strings.SharePoint.MissingElementError(serverRelPathInfo, m_spWebUrl));
                if (ex.ServerErrorTypeName == "System.IO.FileNotFoundException")
                    throw new Interface.FileMissingException(Strings.SharePoint.MissingElementError(serverRelPathInfo, m_spWebUrl));
                else
                    throw;
            }
        }

        #endregion


        #region [Public backend methods]

        public void Test()
        {
            SP.ClientContext ctx = getSpClientContext(true);
            testContextForWeb(ctx, true);
        }

        public List<IFileEntry> List() { return doList(false); }
        private List<IFileEntry> doList(bool useNewContext)
        {
            SP.ClientContext ctx = getSpClientContext(useNewContext);
            try
            {
                SP.Folder remoteFolder = ctx.Web.GetFolderByServerRelativeUrl(m_serverRelPath);
                ctx.Load(remoteFolder, f => f.Exists);
                ctx.Load(remoteFolder, f => f.Files, f => f.Folders);

                wrappedExecuteQueryOnConext(ctx, m_serverRelPath, true);
                if (!remoteFolder.Exists)
                    throw new Interface.FolderMissingException(Strings.SharePoint.MissingElementError(m_serverRelPath, m_spWebUrl));

                List<IFileEntry> files = new List<IFileEntry>(remoteFolder.Folders.Count + remoteFolder.Files.Count);
                foreach (var f in remoteFolder.Folders.Where(ff => ff.Exists))
                {
                    FileEntry fe = new FileEntry(f.Name, -1, f.TimeLastModified, f.TimeLastModified); // f.TimeCreated
                    fe.IsFolder = true;
                    files.Add(fe);
                }
                foreach (var f in remoteFolder.Files.Where(ff => ff.Exists))
                {
                    FileEntry fe = new FileEntry(f.Name, f.Length, f.TimeLastModified, f.TimeLastModified); // f.TimeCreated
                    fe.IsFolder = false;
                    files.Add(fe);
                }
                return files;
            }
            catch (ServerException) { throw; /* rethrow if Server answered */ }
            catch (Interface.FileMissingException) { throw; }
            catch (Interface.FolderMissingException) { throw; }
            catch { if (!useNewContext) /* retry */ return doList(true); else throw; }
            finally { }
        }

        public void Get(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
                Get(remotename, fs);
        }

        public void Get(string remotename, System.IO.Stream stream) { doGet(remotename, stream, false); }
        private void doGet(string remotename, System.IO.Stream stream, bool useNewContext)
        {
            string fileurl = m_serverRelPath + System.Web.HttpUtility.UrlPathEncode(remotename);
            SP.ClientContext ctx = getSpClientContext(useNewContext);
            try
            {
                SP.File remoteFile = ctx.Web.GetFileByServerRelativeUrl(fileurl);
                ctx.Load(remoteFile, f => f.Exists);
                wrappedExecuteQueryOnConext(ctx, fileurl, false);
                if (!remoteFile.Exists)
                    throw new Interface.FileMissingException(Strings.SharePoint.MissingElementError(fileurl, m_spWebUrl));
            }
            catch (ServerException) { throw; /* rethrow if Server answered */ }
            catch (Interface.FileMissingException) { throw; }
            catch (Interface.FolderMissingException) { throw; }
            catch { if (!useNewContext) /* retry */ doGet(remotename, stream, true); else throw; }
            finally { }
            try
            {
                byte[] copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];
                using (var fileInfo = SP.File.OpenBinaryDirect(ctx, fileurl))
                using (var s = fileInfo.Stream)
                    Utility.Utility.CopyStream(s, stream, true, copybuffer);
            }
            finally { }
        }

        public void Put(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                Put(remotename, fs);
        }

        public void Put(string remotename, System.IO.Stream stream) { doPut(remotename, stream, false); }
        private void doPut(string remotename, System.IO.Stream stream, bool useNewContext)
        {
            string fileurl = m_serverRelPath + System.Web.HttpUtility.UrlPathEncode(remotename);
            SP.ClientContext ctx = getSpClientContext(useNewContext);
            try
            {
                SP.Folder remoteFolder = ctx.Web.GetFolderByServerRelativeUrl(m_serverRelPath);
                ctx.Load(remoteFolder, f => f.Exists);
                wrappedExecuteQueryOnConext(ctx, m_serverRelPath, true);
                if (!remoteFolder.Exists)
                    throw new Interface.FolderMissingException(Strings.SharePoint.MissingElementError(m_serverRelPath, m_spWebUrl));

	            UploadFileSlicePerSlice(ctx, remoteFolder, stream, fileurl, 10);
            }
            catch (ServerException) { throw; /* rethrow if Server answered */ }
            catch (Interface.FileMissingException) { throw; }
            catch (Interface.FolderMissingException) { throw; }
            catch { if (!useNewContext) /* retry */ doPut(remotename, stream, true); else throw; }
        }

		/// <summary>
		/// Upload in chunks to bypass filesize limit.
		/// https://msdn.microsoft.com/en-us/library/office/dn904536.aspx
		/// </summary>
		/// <param name="ctx"></param>
		/// <param name="folder"></param>
		/// <param name="sourceFileStream"></param>
		/// <param name="fileName"></param>
		/// <param name="fileChunkSizeInMB"></param>
		/// <returns></returns>
		public SP.File UploadFileSlicePerSlice(ClientContext ctx, Folder folder, Stream sourceFileStream, string fileName, int fileChunkSizeInMB = 10)
		{
			// Each sliced upload requires a unique ID.
			Guid uploadId = Guid.NewGuid();

			// Get the name of the file.
			string uniqueFileName = Path.GetFileName(fileName);

			// File object.
			SP.File uploadFile;

			// Calculate block size in bytes.
			int blockSize = fileChunkSizeInMB * 1024 * 1024;

			// Get the information about the folder that will hold the file.
			ctx.Load(folder, f => f.ServerRelativeUrl);
			ctx.ExecuteQuery();

			// Get the size of the file to be uploaded.
			long fileSize = sourceFileStream.Length;//new FileInfo(fileName).Length;

			if (fileSize <= blockSize)
			{
				// Use regular approach.

				FileCreationInformation fileInfo = new FileCreationInformation();
				fileInfo.ContentStream = sourceFileStream;
				fileInfo.Url = uniqueFileName;
				fileInfo.Overwrite = true;
				uploadFile = folder.Files.Add(fileInfo);
				ctx.Load(uploadFile);
				ctx.ExecuteQuery();
				// Return the file object for the uploaded file.
				return uploadFile;
			}

			// Use large file upload approach.
			using (BinaryReader br = new BinaryReader(sourceFileStream))
			{
				byte[] buffer = new byte[blockSize];
				byte[] lastBuffer = null;
				long fileoffset = 0;
				long totalBytesRead = 0;
				int bytesRead;
				bool first = true;
				bool last = false;

				// Read data from file system in blocks. 
				while ((bytesRead = br.Read(buffer, 0, buffer.Length)) > 0)
				{
					totalBytesRead = totalBytesRead + bytesRead;

					// You've reached the end of the file.
					if (totalBytesRead == fileSize)
					{
						last = true;
						// Copy to a new buffer that has the correct size.
						lastBuffer = new byte[bytesRead];
						Array.Copy(buffer, 0, lastBuffer, 0, bytesRead);
					}

					ClientResult<long> bytesUploaded;
					if (first)
					{
						using (MemoryStream contentStream = new MemoryStream())
						{
							// Add an empty file.
							FileCreationInformation fileInfo = new FileCreationInformation();
							fileInfo.ContentStream = contentStream;
							fileInfo.Url = uniqueFileName;
							fileInfo.Overwrite = true;
							uploadFile = folder.Files.Add(fileInfo);

							// Start upload by uploading the first slice. 
							using (MemoryStream s = new MemoryStream(buffer))
							{
								// Call the start upload method on the first slice.
								bytesUploaded = uploadFile.StartUpload(uploadId, s);
								ctx.ExecuteQuery();
								// fileoffset is the pointer where the next slice will be added.
								fileoffset = bytesUploaded.Value;
							}

							// You can only start the upload once.
							first = false;
						}
					}
					else
					{
						// Get a reference to your file.
						uploadFile = ctx.Web.GetFileByServerRelativeUrl(folder.ServerRelativeUrl + System.IO.Path.AltDirectorySeparatorChar + uniqueFileName);

						if (last)
						{
							// Is this the last slice of data?
							using (MemoryStream s = new MemoryStream(lastBuffer))
							{
								// End sliced upload by calling FinishUpload.
								uploadFile = uploadFile.FinishUpload(uploadId, fileoffset, s);
								ctx.ExecuteQuery();

								// Return the file object for the uploaded file.
								return uploadFile;
							}
						}

						using (MemoryStream s = new MemoryStream(buffer))
						{
							// Continue sliced upload.
							bytesUploaded = uploadFile.ContinueUpload(uploadId, fileoffset, s);
							ctx.ExecuteQuery();
							// Update fileoffset for the next slice.
							fileoffset = bytesUploaded.Value;
						}
					}
				}
			}

			return null;
		}

		public void CreateFolder() { doCreateFolder(false); }
        private void doCreateFolder(bool useNewContext)
        {
            SP.ClientContext ctx = getSpClientContext(useNewContext);
            try
            {
                int pathLengthToWeb = new Utility.Uri(m_spWebUrl).Path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Length;

                string[] folderNames = m_serverRelPath.Substring(0, m_serverRelPath.Length - 1).Split('/');
                folderNames = Array.ConvertAll(folderNames, fold => System.Net.WebUtility.UrlDecode(fold));
                var spfolders = new SP.Folder[folderNames.Length];
                string folderRelPath = "";
                int fi = 0;
                for (; fi < folderNames.Length; fi++)
                {
                    folderRelPath += System.Web.HttpUtility.UrlPathEncode(folderNames[fi]) + "/";
                    if (fi < pathLengthToWeb) continue;
                    var folder = ctx.Web.GetFolderByServerRelativeUrl(folderRelPath);
                    spfolders[fi] = folder;
                    ctx.Load(folder, f => f.Exists);
                    try { wrappedExecuteQueryOnConext(ctx, folderRelPath, true); }
                    catch (FolderMissingException)
                    { break; }
                    if (!folder.Exists) break;
                }

                for (; fi < folderNames.Length; fi++)
                    spfolders[fi] = spfolders[fi - 1].Folders.Add(folderNames[fi]);
                ctx.Load(spfolders[folderNames.Length - 1], f => f.Exists);

                wrappedExecuteQueryOnConext(ctx, m_serverRelPath, true);

                if (!spfolders[folderNames.Length - 1].Exists)
                    throw new Interface.FolderMissingException(Strings.SharePoint.MissingElementError(m_serverRelPath, m_spWebUrl));
            }
            catch (ServerException) { throw; /* rethrow if Server answered */ }
            catch (Interface.FileMissingException) { throw; }
            catch (Interface.FolderMissingException) { throw; }
            catch { if (!useNewContext) /* retry */ doCreateFolder(true); else throw; }
            finally { }
        }

        public void Delete(string remotename) { doDelete(remotename, false); }
        private void doDelete(string remotename, bool useNewContext)
        {
            SP.ClientContext ctx = getSpClientContext(useNewContext);
            try
            {
                string fileurl = m_serverRelPath + System.Web.HttpUtility.UrlPathEncode(remotename);
                SP.File remoteFile = ctx.Web.GetFileByServerRelativeUrl(fileurl);
                ctx.Load(remoteFile);
                wrappedExecuteQueryOnConext(ctx, fileurl, false);
                if (!remoteFile.Exists)
                    throw new Interface.FileMissingException(Strings.SharePoint.MissingElementError(fileurl, m_spWebUrl));

                if (m_deleteToRecycler) remoteFile.Recycle();
                else remoteFile.DeleteObject();

                ctx.ExecuteQuery();

            }
            catch (ServerException) { throw; /* rethrow if Server answered */ }
            catch (Interface.FileMissingException) { throw; }
            catch (Interface.FolderMissingException) { throw; }
            catch { if (!useNewContext) /* retry */ doDelete(remotename, true); else throw; }
            finally { }
        }

        #endregion


        #region IDisposable Members

        public void Dispose()
        {
            try
            {
                if (m_spContext != null)
                    m_spContext.Dispose();
            }
            catch { }
            m_userInfo = null;
        }

        #endregion

    }

}

