#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
using System.Text;

namespace Duplicati.Library.Backend
{   
    /// <summary>
    /// Internal wrapper class for the SFTP exposed by SharpSSH
    /// </summary>
    internal class SFTPCon: Tamir.SharpSsh.Sftp, IDisposable
    {
        public SFTPCon(string hostname, string username)
            : base(hostname, username)
        {
        }

        public SFTPCon(string hostname, string username, string password)
            : base(hostname, username, password)
        {
        }

        protected Tamir.SharpSsh.jsch.ChannelSftp SFTPChannel
        {
            get { return (Tamir.SharpSsh.jsch.ChannelSftp)m_channel; }
        }

        /// <summary>
        /// The internal GetFileList removes the size and modified data
        /// </summary>
        /// <param name="path">The path to list files from</param>
        /// <returns></returns>
        public List<Tamir.SharpSsh.jsch.ChannelSftp.LsEntry> ListFiles(string path)
        {
            List<Tamir.SharpSsh.jsch.ChannelSftp.LsEntry> files = new List<Tamir.SharpSsh.jsch.ChannelSftp.LsEntry>();
            foreach (Tamir.SharpSsh.jsch.ChannelSftp.LsEntry e in this.SFTPChannel.ls(path))
                files.Add(e);

            return files;
        }

        public void Delete(string filename)
        {
            this.SFTPChannel.rm(filename);
        }

        public void SetCurrenDir(string path)
        {
            this.SFTPChannel.cd(path);
        }

        public void Get(string filename, System.IO.Stream outputstream)
        {
            this.SFTPChannel.get(
                filename,
                new OutputStreamWrapper(outputstream)
            );
        }

        public void Put(string filename, System.IO.Stream inputstream)
        {
            this.SFTPChannel.put(
                new Tamir.Streams.InputStreamWrapper(inputstream),
                filename,
                Tamir.SharpSsh.jsch.ChannelSftp.OVERWRITE
            );
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (this.Connected)
                this.Close();
        }

        #endregion
    }
}
