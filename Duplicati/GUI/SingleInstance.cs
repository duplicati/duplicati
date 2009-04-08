#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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

namespace Duplicati.GUI
{
    /// <summary>
    /// The purpose of this class is to ensure that there is ever only one instance of the application running.
    /// This class is based on file-locking, making it much more cross-platform portable than other versions,
    /// that depend on memory communication, such as pipes or shared memory.
    /// </summary>
    public class SingleInstance : IDisposable
    {
        /// <summary>
        /// The folder where control files are placed
        /// </summary>
        private const string CONTROL_DIR = "control_dir";
        /// <summary>
        /// The file that is locked by the first process
        /// </summary>
        private const string CONTROL_FILE = "lock";
        /// <summary>
        /// The prefix on files that communicate with the first instance
        /// </summary>
        private const string COMM_FILE_PREFIX = "other_invocation_";

        /// <summary>
        /// The delegate that is used to inform the first instance of the second invocation
        /// </summary>
        /// <param name="commandlineargs">The commandlinearguments for the second invocation</param>
        public delegate void SecondInstanceDelegate(string[] commandlineargs);

        /// <summary>
        /// When the user tries to launch the application the second time, this event is raised
        /// </summary>
        public event SecondInstanceDelegate SecondInstanceDetected;

        /// <summary>
        /// The file that is locked to prevent other access
        /// </summary>
        private System.IO.FileStream m_file;

        /// <summary>
        /// The folder where control files are placed
        /// </summary>
        private string m_controldir;

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
        /// <param name="path">The path where control files are stored</param>
        public SingleInstance(string appname)
        {
            m_controldir = System.IO.Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appname);
            if (!System.IO.Directory.Exists(m_controldir))
                System.IO.Directory.CreateDirectory(m_controldir);

            m_controldir = System.IO.Path.Combine(m_controldir, CONTROL_DIR);
            if (!System.IO.Directory.Exists(m_controldir))
                System.IO.Directory.CreateDirectory(m_controldir);

            string lockfile = System.IO.Path.Combine(m_controldir, CONTROL_FILE);
            m_file = null;

            try
            {
                m_file = new System.IO.FileStream(lockfile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
            }
            catch
            {
            }

            //If we have write access
            if (m_file != null)
            {
                m_filewatcher = new System.IO.FileSystemWatcher(m_controldir);
                m_filewatcher.Created += new System.IO.FileSystemEventHandler(m_filewatcher_Created);
                m_filewatcher.EnableRaisingEvents = true;

                System.IO.StreamWriter sw = new System.IO.StreamWriter(m_file);
                sw.WriteLine(System.Diagnostics.Process.GetCurrentProcess().Id);
                sw.Flush();
                //Do not dispose the SW, as that will close the file and release the lock

                DateTime startup = System.IO.File.GetLastWriteTime(lockfile);

                //Clean up any files that were created before the app launched
                foreach(string s in System.IO.Directory.GetFiles(m_controldir))
                    if (s != lockfile && System.IO.File.GetCreationTime(s) < startup)
                        try { System.IO.File.Delete(s); }
                        catch { }
            }
            else
            {
                //Wait for the initial process to signal that the filewatcher is activated
                int retrycount = 5;
                while (retrycount > 0 && new System.IO.FileInfo(lockfile).Length == 0)
                {
                    System.Threading.Thread.Sleep(500);
                    retrycount--;
                }

                //HACK: the unix file lock does not allow us to read the file length when the file is locked
                if (new System.IO.FileInfo(lockfile).Length == 0)
                    if (System.Environment.OSVersion.Platform != PlatformID.MacOSX && System.Environment.OSVersion.Platform != PlatformID.Unix)
                        throw new Exception("The file was locked, but had no data");

                //Notify the other process that we have started
                string filename = System.IO.Path.Combine(m_controldir, COMM_FILE_PREFIX + Guid.NewGuid().ToString());

                //Write out the commandline arguments
                using (System.IO.FileStream fs = new System.IO.FileStream(filename, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.None))
                using (System.IO.StreamWriter sw = new System.IO.StreamWriter(fs))
                    foreach(string s in System.Environment.GetCommandLineArgs())
                        sw.WriteLine(s);

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

            do
            {
                try
                {
                    //If the other process deleted the file, just quit
                    if (!System.IO.File.Exists(e.FullPath))
                        return;

                    List<string> args = new List<string>();
                    using (System.IO.FileStream fs = new System.IO.FileStream(e.FullPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
                    using (System.IO.StreamReader sr = new System.IO.StreamReader(fs))
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

                try { System.IO.File.Delete(m_file.Name); }
                catch { }

                m_file = null;
            }
        }

        #endregion
    }
}
