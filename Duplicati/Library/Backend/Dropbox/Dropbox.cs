using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using Duplicati.Library.Interface;
using Newtonsoft.Json.Linq;

namespace Duplicati.Library.Backend
{
    public class Dropbox : IBackend, IStreamingBackend
    {
        private const string AUTHID_OPTION = "authid";

        

        private const int MAX_FILE_LIST = 10000;

        private string m_path,m_accesToken;
        private DropboxHelper dbx;

        public Dropbox()
        {

        }

        public Dropbox(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);

            m_path = Library.Utility.Uri.UrlDecode(uri.HostAndPath);
            if (m_path.Length != 0 && !m_path.StartsWith("/"))
                m_path = "/" + m_path;

            if (m_path.EndsWith("/"))
                m_path = m_path.Substring(0, m_path.Length - 1);

            if (options.ContainsKey(AUTHID_OPTION))
                m_accesToken = options[AUTHID_OPTION];

            dbx = new DropboxHelper(m_accesToken);
        }

        public void Dispose()
        {
            // do nothing
        }

        public string DisplayName
        {
            get { return "Dropbox"; }
        }

        public string ProtocolKey
        {
            get { return "dropbox"; }
        }




        public List<IFileEntry> List()
        {
            try
            {
                ListFolderResult lfr = dbx.ListFiles(m_path);

                
                List<IFileEntry> list = new List<IFileEntry>();
              
                foreach (MetaData md in lfr.entries)
                {
                    FileEntry ife = new FileEntry(md.name);
                    if (md.IsFile)
                    {
                        ife.IsFolder = false;
                        ife.Size = (long)md.size;
                    }
                    else
                    {
                        ife.IsFolder = true;
                    }

                    list.Add(ife);
                }
                if (lfr.has_more)
                {
                    do
                    {
                        lfr = dbx.ListFilesContinue(lfr.cursor);

                        foreach (MetaData md in lfr.entries)
                        {
                            FileEntry ife = new FileEntry(md.name);
                            if (md.IsFile)
                            {
                                ife.IsFolder = false;
                                ife.Size = (long) md.size;
                            }
                            else
                            {
                                ife.IsFolder = true;
                            }

                            list.Add(ife);
                        }
                    } while (lfr.has_more);
                }

                return list;
            }
            catch (DropboxException de)
            {

                if (de.errorJSON["error"][".tag"].ToString() == "path")
                {
                    if (de.errorJSON["error"]["path"][".tag"].ToString() == "not_found")
                    {
                        throw new FolderMissingException();
                    }
                    else
                    {
                        throw;
                    }
                }
                else
                {
                    throw;
                }

            }

            
        }
        
        



        public void Put(string remotename, string filename)
        {

            using(FileStream fs = System.IO.File.OpenRead(filename))
                Put(remotename,fs);
            
           
        }

        public void Get(string remotename, string filename)
        {
            using(FileStream fs = System.IO.File.Create(filename))
                Get(remotename, fs);

        }

        public void Delete(string remotename)
        {
            try
            {
                string path = String.Format("{0}/{1}", m_path, remotename);
                dbx.Delete(path);
            }
            catch (DropboxException de)
            {
                // we can catch some events here and convert them to Duplicati exceptions
                throw;
            }
        }

        public IList<ICommandLineArgument> SupportedCommands { get; private set; }
        
        public string Description { get; private set; }

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            try
            {
                dbx.CreateFolder(m_path);
            }
            catch (DropboxException de)
            {

                if (de.errorJSON["error"][".tag"].ToString() == "path")
                {
                    if (de.errorJSON["error"]["path"][".tag"].ToString() == "conflict")
                    {
                        throw new FolderAreadyExistedException();
                    }
                    else
                        throw;
                }
                else
                {
                    throw;
                }

            }
        }

        public void Put(string remotename, Stream stream)
        {
            try
            {
                string path = String.Format("{0}/{1}", m_path, remotename);
                dbx.UploadFile(path, stream);
            }
            catch (DropboxException de)
            {
                // we can catch some events here and convert them to Duplicati exceptions
                throw;
            }
        }

        public void Get(string remotename, Stream stream)
        {
            try
            {
                string path = String.Format("{0}/{1}", m_path, remotename);
                dbx.DownloadFile(path, stream);
            }
            catch (DropboxException de)
            {
                // we can catch some events here and convert them to Duplicati exceptions
                throw;
            }
        }
    }
}