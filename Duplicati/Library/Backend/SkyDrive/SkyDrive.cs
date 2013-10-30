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
    public class SkyDrive : IBackend, IStreamingBackend
    {
        private const string USERNAME_OPTION = "passport-username";
        private const string PASSWORD_OPTION = "passport-password";

        private string m_username;
        private string m_password;
        private string m_rootfolder;
        private string m_prefix;

        private SkyDriveSession m_session;

        public SkyDrive() { }

        public SkyDrive(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            uri.RequireHost();
            
            m_rootfolder = uri.Host;
            m_prefix = "/" + uri.Path;
            if (!m_prefix.EndsWith("/"))
                m_prefix += "/";

            if (options.ContainsKey("auth-username"))
                m_username = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                m_password = options["auth-password"];
            if (options.ContainsKey(USERNAME_OPTION))
                m_username = options[USERNAME_OPTION];
            if (options.ContainsKey(PASSWORD_OPTION))
                m_password = options[PASSWORD_OPTION];
            if (!string.IsNullOrEmpty(uri.Username))
                m_username = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                m_password = uri.Password;

            if (string.IsNullOrEmpty(m_username))
                throw new Exception(Strings.SkyDrive.MissingUsernameError);
            if (string.IsNullOrEmpty(m_password))
                throw new Exception(Strings.SkyDrive.MissingPasswordError);
        }

        private SkyDriveSession CreateSession(bool createFolders)
        {
            if (createFolders)
                return new SkyDriveSession(m_username, m_password, m_rootfolder, m_prefix, createFolders);

            if (m_session == null)
                m_session = new SkyDriveSession(m_username, m_password, m_rootfolder, m_prefix, createFolders);

            return m_session;
        }

        #region IBackend Members

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            using (SkyDriveSession session = CreateSession(true))
            { }
        }

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
            SkyDriveSession session = CreateSession(false);
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
            CreateSession(false).DeleteFile(remotename);
        }        

        public IList<ICommandLineArgument> SupportedCommands
        {
            get {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.SkyDrive.DescriptionAuthUsernameShort, Strings.SkyDrive.DescriptionAuthUsernameLong),
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.SkyDrive.DescriptionAuthPasswordShort, Strings.SkyDrive.DescriptionAuthPasswordLong),
                    new CommandLineArgument(USERNAME_OPTION, CommandLineArgument.ArgumentType.Password, Strings.SkyDrive.DescriptionPassportUsernameShort, Strings.SkyDrive.DescriptionPassportUsernameLong, null, new string[] {"auth-username"}),
                    new CommandLineArgument(PASSWORD_OPTION, CommandLineArgument.ArgumentType.String, Strings.SkyDrive.DescriptionPassportPasswordShort, Strings.SkyDrive.DescriptionPassportPasswordLong, null, new string[] {"auth-password"}),
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
            CreateSession(false).UploadFile(remotename, stream);
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            using (System.Net.HttpWebResponse resp = CreateSession(false).DownloadFile(remotename))
                using (System.IO.Stream s = resp.GetResponseStream())
                    Utility.Utility.CopyStream(s, stream);
        }

        #endregion
    }
}
