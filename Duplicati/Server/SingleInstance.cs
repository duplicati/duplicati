#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
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
using System.Text;
using Duplicati.Library.Common;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;

namespace Duplicati.Server
{
    /// <summary>
    /// The purpose of this class is to ensure that there is ever only one instance of the application running.
    /// This class is based on file-locking, making it much more cross-platform portable than other versions,
    /// that depend on memory communication, such as pipes or shared memory.
    /// </summary>
    public class SingleInstance : IDisposable
    {
        /// <summary>
        /// An exception that can be thrown to indicate a second instance was running
        /// </summary>
        [Serializable]
        public class MultipleInstanceException : Exception
        {
            /// <summary>
            /// Constructs the new exception
            /// </summary>
            public MultipleInstanceException()
                : base()
            {
            }

            /// <summary>
            /// Constructs the new exception
            /// </summary>
            /// <param name="message">The message</param>
            public MultipleInstanceException(string message)
                : base(message)
            {
            }

            /// <summary>
            /// Constructs the new exception
            /// </summary>
            /// <param name="message">The message</param>
            /// <param name="innerException">The inner exception</param>
            public MultipleInstanceException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }

        /// <summary>
        /// The folder where control files are placed
        /// </summary>
        /// <remarks>
        /// This directory is referenced in the common filters in FilterGroups.cs.
        /// If it is ever changed, the filter should be updated as well.
        /// </remarks>
        private const string CONTROL_DIR = "control_dir_v2";
        /// <summary>
        /// The file that is locked by the first process
        /// </summary>
        private const string CONTROL_FILE = "lock_v2";
        /// <summary>
        /// The prefix on files that communicate with the first instance
        /// </summary>
        private const string COMM_FILE_PREFIX = "other_invocation_v2_";

        /// <summary>
        /// The delegate that is used to inform the first instance of the second invocation
        /// </summary>
        /// <param name="commandlineargs">The command-line arguments for the second invocation</param>
        public delegate void SecondInstanceDelegate(string[] commandlineargs);

        /// <summary>
        /// When the user tries to launch the application the second time, this event is raised
        /// </summary>
        public event SecondInstanceDelegate SecondInstanceDetected;

        /// <summary>
        /// The file that is locked to prevent other access
        /// </summary>
        private IDisposable m_file;

        /// <summary>
        /// The folder where control files are placed
        /// </summary>
        private readonly string m_controldir;

        /// <summary>
        /// The full path to the locking file
        /// </summary>
        private readonly string m_lockfilename;

        /// <summary>
        /// The watcher that allows interprocess communication
        /// </summary>
        private System.IO.FileSystemWatcher m_filewatcher;

        /// <summary>
        /// Gets a value indicating if this is the first instance of the application
        /// </summary>
        public bool IsFirstInstance { get { return m_file != null; } }

        /// <summary>
        /// Constructs a new SingleInstance object
        /// </summary>
        /// <param name="basefolder">The folder in which the control file structure is placed</param>
        ///
        public SingleInstance(string basefolder)
        {
            if (!System.IO.Directory.Exists(basefolder))
                System.IO.Directory.CreateDirectory(basefolder);

            m_controldir = System.IO.Path.Combine(basefolder, CONTROL_DIR);
            if (!System.IO.Directory.Exists(m_controldir))
                System.IO.Directory.CreateDirectory(m_controldir);

            m_lockfilename = System.IO.Path.Combine(m_controldir, CONTROL_FILE);
            m_file = null;
            
            System.IO.Stream temp_fs = null;

            try
            {
                if (Platform.IsClientPosix)
                    temp_fs = UnixSupport.File.OpenExclusive(m_lockfilename, System.IO.FileAccess.Write);
                else
                    temp_fs = System.IO.File.Open(m_lockfilename, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
                
                if (temp_fs != null)
                {
                    System.IO.StreamWriter sw = new System.IO.StreamWriter(temp_fs);
                    sw.WriteLine(System.Diagnostics.Process.GetCurrentProcess().Id);
                    sw.Flush();
                    //Do not dispose sw as that would dispose the stream
                    m_file = temp_fs;
                }
            }
            catch
            {
                if (temp_fs != null)
                    try { temp_fs.Dispose(); }
                    catch {}
            }

            //If we have write access
            if (m_file != null)
            {
                m_filewatcher = new System.IO.FileSystemWatcher(m_controldir);
                m_filewatcher.Created += new System.IO.FileSystemEventHandler(m_filewatcher_Created);
                m_filewatcher.EnableRaisingEvents = true;

                DateTime startup = System.IO.File.GetLastWriteTime(m_lockfilename);

                //Clean up any files that were created before the app launched
                foreach(string s in SystemIO.IO_OS.GetFiles(m_controldir))
                    if (s != m_lockfilename && System.IO.File.GetCreationTime(s) < startup)
                        try { System.IO.File.Delete(s); }
                        catch { }
            }
            else
            {
                //Wait for the initial process to signal that the filewatcher is activated
                int retrycount = 5;
                while (retrycount > 0 && new System.IO.FileInfo(m_lockfilename).Length == 0)
                {
                    System.Threading.Thread.Sleep(500);
                    retrycount--;
                }

                //HACK: the unix file lock does not allow us to read the file length when the file is locked
                if (new System.IO.FileInfo(m_lockfilename).Length == 0)
                    if (!Platform.IsClientPosix)
                        throw new Exception("The file was locked, but had no data");

                //Notify the other process that we have started
                string filename = System.IO.Path.Combine(m_controldir, COMM_FILE_PREFIX + Guid.NewGuid().ToString());

                //Write out the commandline arguments
                string[] cmdargs = System.Environment.GetCommandLineArgs();
                using (System.IO.StreamWriter sw = new System.IO.StreamWriter(Platform.IsClientPosix ? UnixSupport.File.OpenExclusive(filename, System.IO.FileAccess.Write) : new System.IO.FileStream(filename, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.None)))
                    for (int i = 1; i < cmdargs.Length; i++) //Skip the first, as that is the filename
                        sw.WriteLine(cmdargs[i]);

                //Wait for the other process to delete the file, indicating that it is processed
                retrycount = 5;
                while (retrycount > 0 && System.IO.File.Exists(filename))
                {
                    System.Threading.Thread.Sleep(500);
                    retrycount--;
                }

                //This may happen if the other process is closing as we write the command
                if (System.IO.File.Exists(filename))
                {
                    //Try to clean up, so the other process does not spuriously show this
                    try { System.IO.File.Delete(filename); }
                    catch { }

                    throw new Exception("The lock file was locked, but the locking process did not respond to the start command");
                }
            }
        }

        /// <summary>
        /// The event that is raised when a new file is created in the control dir
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">The file event arguments</param>
        private void m_filewatcher_Created(object sender, System.IO.FileSystemEventArgs e)
        {
            //Retry 5 times if the other process is slow on releasing the file lock
            int retrycount = 5;
            //Indicator and holder of arguments passed
            string[] commandline = null;

            //HACK: Linux has some locking issues
            //The problem is that there is no atomic open-and-lock operation, so the other process
            // needs a little time to create+lock the file. This is not really a fix, but an
            // ugly workaround. This functionality is only used to allow a new instance to signal
            // the running instance, so errors here would only affect that functionality
            if (Platform.IsClientPosix)
                System.Threading.Thread.Sleep(1000);

            do
            {
                try
                {
                    //If the other process deleted the file, just quit
                    if (!System.IO.File.Exists(e.FullPath))
                        return;

                    List<string> args = new List<string>();
                    using (System.IO.StreamReader sr = new System.IO.StreamReader(Platform.IsClientPosix ? UnixSupport.File.OpenExclusive(e.FullPath, System.IO.FileAccess.ReadWrite) : new System.IO.FileStream(e.FullPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None)))
                    while(!sr.EndOfStream)
                        args.Add(sr.ReadLine());

                    commandline = args.ToArray();

                    //Remove the file to notify the other process that we have correctly processed the call
                    System.IO.File.Delete(e.FullPath);
                }
                catch
                {
                }

                //If file-reading failed, wait a little before retry
                if (commandline == null)
                {
                    System.Threading.Thread.Sleep(500);
                    retrycount--;
                }

            } while (retrycount > 0 && commandline == null);

            //If this happens, we detected the file, but was unable to read it's contents
            if (commandline == null)
            {
                //There is nothing we can do :(
            }
            else
            {
                //If we read the data but did not delete the file, the other end still hangs
                //and waits for us to clean up, so try again.
                retrycount = 5;
                while (retrycount > 0 && System.IO.File.Exists(e.FullPath))
                {
                    try
                    {
                        System.IO.File.Delete(e.FullPath);
                    }
                    catch
                    {
                        //Wait before the retry
                        System.Threading.Thread.Sleep(500);
                    }

                    retrycount--;
                }

                //If this happens, the other process will give an error message
                if (System.IO.File.Exists(e.FullPath))
                {
                    //There is nothing we can do :(
                }

                //Finally inform this instance about the call
                if (SecondInstanceDetected != null)
                    SecondInstanceDetected(commandline); 
            }

        }

        #region IDisposable Members

        public void Dispose()
        {
            if (m_filewatcher != null)
            {
                m_filewatcher.EnableRaisingEvents = false;
                m_filewatcher.Created -= new System.IO.FileSystemEventHandler(m_filewatcher_Created);
                m_filewatcher.Dispose();
                m_filewatcher = null;
            }

            if (m_file != null)
            {
                m_file.Dispose();

                try { System.IO.File.Delete(m_lockfilename); }
                catch { }

                m_file = null;
            }
        }

        #endregion
    }
}
