//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using CG.Web.MegaApiClient;

namespace Duplicati.Library.Backend.Mega
{
    public class MegaBackend: IBackend, IStreamingBackend
    {
        private string m_username = null;
        private string m_password = null;
        private Dictionary<string, List<INode>> m_filecache;
        private INode m_currentFolder = null;
        private string m_prefix = null;

        private MegaApiClient m_client;

        public MegaBackend()
        {
        }

        private MegaApiClient Client
        {
            get
            {
                if (m_client == null)
                {
                    var cl = new MegaApiClient();
                    cl.Login(m_username, m_password);
                    m_client = cl;
                }

                return m_client;
            }
        }

        public MegaBackend(string url, Dictionary<string, string> options)
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
                throw new UserInformationException(Strings.MegaBackend.NoUsernameError);
            if (string.IsNullOrEmpty(m_password))
                throw new UserInformationException(Strings.MegaBackend.NoPasswordError);

            m_prefix = uri.HostAndPath ?? "";
        }

        private void GetCurrentFolder(bool autocreate = false)
        {
            var parts = m_prefix.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            var nodes = Client.GetNodes();
            INode parent = nodes.Where(x => x.Type == NodeType.Root).First();

            foreach(var n in parts)
            {
                var item = nodes.Where(x => x.Name == n && x.Type == NodeType.Directory && x.ParentId == parent.Id).FirstOrDefault();
                if (item == null)
                {
                    if (!autocreate)
                        throw new FolderMissingException();

                    item = Client.CreateFolder(n, parent);
                }

                parent = item;
            }

            m_currentFolder = parent;

            ResetFileCache(nodes);
        }

        private INode CurrentFolder
        {
            get
            {
                if (m_currentFolder == null)
                    GetCurrentFolder(false);

                return m_currentFolder;
            }
        }

        private INode GetFileNode(string name)
        {
            if (m_filecache != null && m_filecache.ContainsKey(name))
                return m_filecache[name].OrderByDescending(x => x.LastModificationDate).First();

            ResetFileCache();

            if (m_filecache != null && m_filecache.ContainsKey(name))
                return m_filecache[name].OrderByDescending(x => x.LastModificationDate).First();
            
            throw new FileMissingException();
        }

        private void ResetFileCache(IEnumerable<INode> list = null)
        {
            if (m_currentFolder == null)
            {
                GetCurrentFolder(false);
            }
            else
            {
                m_filecache = 
                    (list ?? Client.GetNodes()).Where(x => x.Type == NodeType.File && x.ParentId == CurrentFolder.Id)
                        .GroupBy(x => x.Name, x => x, (k, g) => new KeyValuePair<string, List<INode>>(k, g.ToList()))
                        .ToDictionary(x => x.Key, x => x.Value);
            }
        }

        #region IStreamingBackend implementation

        public void Put(string remotename, System.IO.Stream stream)
        {
            try
            {
                if (m_filecache == null)
                    ResetFileCache();

                var el = Client.Upload(stream, remotename, CurrentFolder);
                if (m_filecache.ContainsKey(remotename))
                    Delete(remotename);

                m_filecache[remotename] = new List<INode>();
                m_filecache[remotename].Add(el);
            }
            catch
            {
                m_filecache = null;
                throw;
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            using(var s = Client.Download(GetFileNode(remotename)))
                Library.Utility.Utility.CopyStream(s, stream);
        }

        #endregion

        #region IBackend implementation

        public List<IFileEntry> List()
        {
            if (m_filecache == null)
                ResetFileCache();
            
            return (
                from n in m_filecache.Values
                let item = n.OrderByDescending(x => x.LastModificationDate).First()
                select (IFileEntry)new FileEntry(item.Name, item.Size, item.LastModificationDate, item.LastModificationDate)
            ).ToList();
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
                if (m_filecache == null || !m_filecache.ContainsKey(remotename))
                    ResetFileCache();

                if (!m_filecache.ContainsKey(remotename))
                    throw new FileMissingException();

                foreach(var n in m_filecache[remotename])
                    Client.Delete(n, false);

                m_filecache.Remove(remotename);
            }
            catch
            {
                m_filecache = null;
                throw;
            }
        }

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            GetCurrentFolder(true);
        }

        public string DisplayName
        {
            get
            {
                return Strings.MegaBackend.DisplayName;
            }
        }

        public string ProtocolKey
        {
            get
            {
                return "mega";
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.MegaBackend.AuthPasswordDescriptionShort, Strings.MegaBackend.AuthPasswordDescriptionLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.MegaBackend.AuthUsernameDescriptionShort, Strings.MegaBackend.AuthUsernameDescriptionLong),
                });
            }
        }

        public string Description
        {
            get
            {
                return Strings.MegaBackend.Description;
            }
        }

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
        }

        #endregion
    }
}

