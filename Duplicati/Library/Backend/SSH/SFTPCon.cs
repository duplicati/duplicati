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
