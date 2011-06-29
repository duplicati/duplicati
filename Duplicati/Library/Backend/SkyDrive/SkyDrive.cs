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
    public class SkyDrive : IBackend_v2, IStreamingBackend, IBackendGUI
    {
        private const string USERNAME_OPTION = "passport-username";
        private const string PASSWORD_OPTION = "passport-password";

        private static readonly Regex URL_PARSER = new Regex("skydrive://(?<rootfolder>[^/]+)(?<prefix>.*)");

        private string m_username;
        private string m_password;
        private string m_rootfolder;
        private string m_prefix;

        public SkyDrive() { }

        public SkyDrive(string url, Dictionary<string, string> options)
        {
            Match m = URL_PARSER.Match(url);
            if (!m.Success)
                throw new Exception(string.Format(Strings.SkyDrive.InvalidUrlError, url));

            m_rootfolder = m.Groups["rootfolder"].Value;
            m_prefix = m.Groups["prefix"].Value;
            if (!m_prefix.EndsWith("/"))
                m_prefix += "/";

            if (options.ContainsKey("ftp-username"))
                m_username = options["ftp-username"];
            if (options.ContainsKey("ftp-password"))
                m_password = options["ftp-password"];
            if (options.ContainsKey(USERNAME_OPTION))
                m_username = options[USERNAME_OPTION];
            if (options.ContainsKey(PASSWORD_OPTION))
                m_password = options[PASSWORD_OPTION];


            if (string.IsNullOrEmpty(m_username))
                throw new Exception(Strings.SkyDrive.MissingUsernameError);
            if (string.IsNullOrEmpty(m_password))
                throw new Exception(Strings.SkyDrive.MissingPasswordError);
        }

        private SkyDriveSession CreateSession(bool createFolders)
        {
            //TODO: Once persistent connections are supported, this instance should be cached
            return new SkyDriveSession(m_username, m_password, m_rootfolder, m_prefix, createFolders);
        }

        #region IBackend_v2 Members

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            using (SkyDriveSession session = CreateSession(true))
            { }
        }

        #endregion

        #region IBackend Members

        public string DisplayName
        {
            get { return Strings.SkyDrive.Displayname; }
        }

        public string ProtocolKey
        {
            get { return "skydrive"; }
        }

        public List<IFileEntry> List()
        {
            using (SkyDriveSession session = CreateSession(false))
                return session.ListFolderItems(session.FolderCID);
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
            using(SkyDriveSession session = CreateSession(false))
            using (System.Net.HttpWebResponse resp = session.DeleteFile(remotename))
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                    throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
            }
        }        

        public IList<ICommandLineArgument> SupportedCommands
        {
            get {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.String, Strings.SkyDrive.DescriptionFTPPasswordShort, Strings.SkyDrive.DescriptionFTPPasswordLong),
                    new CommandLineArgument("ftp-username", CommandLineArgument.ArgumentType.String, Strings.SkyDrive.DescriptionFTPUsernameShort, Strings.SkyDrive.DescriptionFTPUsernameLong),
                    new CommandLineArgument(USERNAME_OPTION, CommandLineArgument.ArgumentType.String, Strings.SkyDrive.DescriptionPassportPasswordShort, Strings.SkyDrive.DescriptionPassportPasswordLong, null, new string[] {"ftp-password"}),
                    new CommandLineArgument(PASSWORD_OPTION, CommandLineArgument.ArgumentType.String, Strings.SkyDrive.DescriptionPassportUsernameShort, Strings.SkyDrive.DescriptionPassportUsernameLong, null, new string[] {"ftp-username"}),
                });
            }
        }

        public string Description
        {
            get { return Strings.SkyDrive.Description; }
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
            using (SkyDriveSession session = CreateSession(false))
            using (System.Net.HttpWebResponse resp = session.UploadFile(remotename, stream))
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                    throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            using (SkyDriveSession session = CreateSession(false))
            using (System.Net.HttpWebResponse resp = session.DownloadFile(remotename))
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                    throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                using (System.IO.Stream s = resp.GetResponseStream())
                    Utility.Utility.CopyStream(s, stream);
            }
        }

        #endregion

        #region IGUIControl Members

        public string PageTitle
        {
            get { return SkyDriveUI.PageTitle; }
        }

        public string PageDescription
        {
            get { return SkyDriveUI.PageDescription; }
        }

        public System.Windows.Forms.Control GetControl(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
        {
            return new SkyDriveUI(options);
        }

        public void Leave(System.Windows.Forms.Control control)
        {
            ((SkyDriveUI)control).Save(false);
        }

        public bool Validate(System.Windows.Forms.Control control)
        {
            return ((SkyDriveUI)control).Save(true);
        }

        public string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            return SkyDriveUI.GetConfiguration(guiOptions, commandlineOptions);
        }

        #endregion
    }
}
