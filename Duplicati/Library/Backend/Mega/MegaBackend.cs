// Copyright (C) 2024, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using CG.Web.MegaApiClient;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OtpNet;

namespace Duplicati.Library.Backend.Mega
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class MegaBackend: IBackend, IStreamingBackend
    {
        private readonly string m_username = null;
        private readonly string m_password = null;
        private readonly string m_twoFactorKey = null;
        private Dictionary<string, List<INode>> m_filecache;
        private INode m_currentFolder = null;
        private readonly string m_prefix = null;

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
                    if (m_twoFactorKey == null)
                        cl.Login(m_username, m_password);
                    else
                    {
                        var totp = new Totp(Base32Encoding.ToBytes(m_twoFactorKey)).ComputeTotp();
                        cl.Login(m_username, m_password, totp);
                    }
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
            if (options.ContainsKey("auth-two-factor-key"))
                m_twoFactorKey = options["auth-two-factor-key"];

            if (!string.IsNullOrEmpty(uri.Username))
                m_username = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                m_password = uri.Password;

            if (string.IsNullOrEmpty(m_username))
                throw new UserInformationException(Strings.MegaBackend.NoUsernameError, "MegaNoUsername");
            if (string.IsNullOrEmpty(m_password))
                throw new UserInformationException(Strings.MegaBackend.NoPasswordError, "MegaNoPassword");

            m_prefix = uri.HostAndPath ?? "";
        }

        private void GetCurrentFolder(bool autocreate = false)
        {
            var parts = m_prefix.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            var nodes = Client.GetNodes();
            INode parent = nodes.First(x => x.Type == NodeType.Root);

            foreach(var n in parts)
            {
                var item = nodes.FirstOrDefault(x => x.Name == n && x.Type == NodeType.Directory && x.ParentId == parent.Id);
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
                return m_filecache[name].OrderByDescending(x => x.ModificationDate).First();

            ResetFileCache();

            if (m_filecache != null && m_filecache.ContainsKey(name))
                return m_filecache[name].OrderByDescending(x => x.ModificationDate).First();
            
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

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            try
            {
                if (m_filecache == null)
                    ResetFileCache();

                var el = await Client.UploadAsync(stream, remotename, CurrentFolder, new Progress(), null, cancelToken);
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

        public IEnumerable<IFileEntry> List()
        {
            if (m_filecache == null)
                ResetFileCache();
            
            return
                from n in m_filecache.Values
                let item = n.OrderByDescending(x => x.ModificationDate).First()
                select new FileEntry(item.Name, item.Size, item.ModificationDate ?? new DateTime(0), item.ModificationDate ?? new DateTime(0));
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
            this.TestList();
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
                    new CommandLineArgument("auth-two-factor-key", CommandLineArgument.ArgumentType.Password, Strings.MegaBackend.AuthTwoFactorKeyDescriptionShort, Strings.MegaBackend.AuthTwoFactorKeyDescriptionLong),
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

        public string[] DNSName
        {
            get { return null; }
        }

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
        }

        #endregion

        private class Progress : IProgress<double>
        {
            public void Report(double value)
            {
                // No implementation as we have already wrapped the stream in our own progress reporting stream
            }
        }
    }
}
