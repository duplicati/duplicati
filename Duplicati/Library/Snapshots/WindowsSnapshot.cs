using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Alphaleonis.Win32.Vss;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// This class encapsulates all access to the Windows Volume Shadow copy Services,
    /// implementing the disposable patterns to ensure correct release of resources.
    /// 
    /// The class presents all files and folders with their regular filenames to the caller,
    /// and internally handles the conversion to the shadow path.
    /// </summary>
    public class WindowsSnapshot : ISnapshotService
    {
        /// <summary>
        /// The main reference to the backup controller
        /// </summary>
        private IVssBackupComponents m_backup;
        /// <summary>
        /// The id of the ongoing snapshot
        /// </summary>
        private Guid m_snapshotId;
        /// <summary>
        /// The list of paths that will be shadow copied
        /// </summary>
        private string[] m_sourcepaths;
        /// <summary>
        /// The list of snapshot ids for each volume, key is the path root, eg C:\.
        /// The dictionary is case insensitive
        /// </summary>
        private Dictionary<string, Guid> m_volumes;
        /// <summary>
        /// Commonly used string element
        /// </summary>
        private static string SLASH = Path.DirectorySeparatorChar.ToString();

        /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="sourcepaths">The folders that are about to be backed up</param>
        /// <param name="options">A set of commandline options</param>
        public WindowsSnapshot(string[] sourcepaths, Dictionary<string, string> options)
        {
            try
            {
                //Substitute for calling VssUtils.LoadImplementation(), as we have the dlls outside the GAC
                string alphadir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "alphavss");
                string alphadll = System.IO.Path.Combine(alphadir, VssUtils.GetPlatformSpecificAssemblyName().Name + ".dll");
                IVssImplementation vss = (IVssImplementation)System.Reflection.Assembly.LoadFile(alphadll).CreateInstance("Alphaleonis.Win32.Vss.VssImplementation");

                List<Guid> excludedWriters = new List<Guid>();
                if (options.ContainsKey("vss-exclude-writers"))
                {
                    foreach (string s in options["vss-exclude-writers"].Split(';'))
                        if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0)
                            excludedWriters.Add(new Guid(s));
                }

                //Prepare the backup
                m_backup = vss.CreateVssBackupComponents();
                m_backup.InitializeForBackup(null);

                if (excludedWriters.Count > 0)
                    m_backup.DisableWriterClasses(excludedWriters.ToArray());

                m_snapshotId = m_backup.StartSnapshotSet();

                m_sourcepaths = new string[sourcepaths.Length];

                for(int i = 0; i < m_sourcepaths.Length; i++)
                    m_sourcepaths[i] = Core.Utility.AppendDirSeparator(sourcepaths[i]);

                try
                {
                    //Gather information on all Vss writers
                    using (IVssAsync async = m_backup.GatherWriterMetadata())
                        async.Wait();
                    m_backup.FreeWriterMetadata();
                }
                catch
                {
                    try { m_backup.FreeWriterMetadata(); }
                    catch { }

                    throw;
                }

                //Figure out which volumes are in the set
                m_volumes = new Dictionary<string, Guid>(StringComparer.InvariantCultureIgnoreCase);
                foreach (string s in m_sourcepaths)
                {
                    string drive = Path.GetPathRoot(s);
                    if (!m_volumes.ContainsKey(drive))
                    {
                        if (!m_backup.IsVolumeSupported(drive))
                            throw new VssVolumeNotSupportedException(drive);

                        m_volumes.Add(drive, m_backup.AddToSnapshotSet(drive)); 
                    }
                }

                //Signal that we want to do a backup
                m_backup.SetBackupState(false, true, VssBackupType.Full, false);

                //Make all writers aware that we are going to do the backup
                using (IVssAsync async = m_backup.PrepareForBackup())
                    async.Wait();

                //Create the shadow volumes
                using (IVssAsync async = m_backup.DoSnapshotSet())
                    async.Wait();
            }
            catch
            {
                //In case we fail in the constructor, we do not want a snapshot to be active
                try { Dispose(); }
                catch { }

                throw;
            }
        }

#if DEBUG
        /// <summary>
        /// Returns all files found in the shadow copies
        /// </summary>
        /// <returns>A list of filenames to files found in the shadow volumes</returns>
        public List<string> AllFiles()
        {
            collector c = new collector();

            EnumerateFilesAndFolders(null, c.callback);

            return c.files;
        }

        /// <summary>
        /// Internal class for collecting filenames
        /// </summary>
        private class collector
        {
            /// <summary>
            /// The list of files
            /// </summary>
            public List<string> files = new List<string>();
            /// <summary>
            /// The list of folders
            /// </summary>
            public List<string> folders = new List<string>();
            /// <summary>
            /// The list of error paths
            /// </summary>
            public List<string> errors = new List<string>();

            /// <summary>
            /// The callback function invoked when collecting data
            /// </summary>
            /// <param name="rootpath">The source folder</param>
            /// <param name="path">The full entry path</param>
            /// <param name="status">The entry type</param>
            public void callback(string rootpath, string path, Core.Utility.EnumeratedFileStatus status)
            {
                if (status == Duplicati.Library.Core.Utility.EnumeratedFileStatus.File)
                    files.Add(path);
                else if (status == Duplicati.Library.Core.Utility.EnumeratedFileStatus.Folder)
                    folders.Add(path);
                else
                    errors.Add(path);
            }
        }
#endif

        #region Private functions

        /// <summary>
        /// A callback function that takes a non-shadow path to a folder,
        /// and returns all folders found in a non-shadow path format.
        /// </summary>
        /// <param name="folder">The non-shadow path of the folder to list</param>
        /// <returns>A list of non-shadow paths</returns>
        private string[] ListFolders(string folder)
        {
            string root = Core.Utility.AppendDirSeparator(Path.GetPathRoot(folder));
            string volumePath = Core.Utility.AppendDirSeparator(GetSnapshotPath(root));

            string[] tmp = Alphaleonis.Win32.Filesystem.Directory.GetDirectories(GetSnapshotPath(folder));
            for (int i = 0; i < tmp.Length; i++)
                tmp[i] = root + tmp[i].Substring(volumePath.Length);
            return tmp;
        }


        /// <summary>
        /// A callback function that takes a non-shadow path to a folder,
        /// and returns all files found in a non-shadow path format.
        /// </summary>
        /// <param name="folder">The non-shadow path of the folder to list</param>
        /// <returns>A list of non-shadow paths</returns>
        private string[] ListFiles(string folder)
        {
            string root = Core.Utility.AppendDirSeparator(Path.GetPathRoot(folder));
            string volumePath = Core.Utility.AppendDirSeparator(GetSnapshotPath(root));

            string[] tmp = Alphaleonis.Win32.Filesystem.Directory.GetFiles(GetSnapshotPath(folder));
            for (int i = 0; i < tmp.Length; i++)
                tmp[i] = root + tmp[i].Substring(volumePath.Length);
            return tmp;
        }

        /// <summary>
        /// Helper function to translate a local path into the corresponding shadow volume path
        /// </summary>
        /// <param name="localPath">The local path to convert</param>
        /// <returns>The corresponding shadow volume path</returns>
        private string GetSnapshotPath(string localPath)
        {
            if (!Path.IsPathRooted(localPath))
                throw new InvalidOperationException();

            string root = Path.GetPathRoot(localPath);

            if (!m_volumes.ContainsKey(root))
                throw new InvalidOperationException();

            localPath = localPath.Replace(root, String.Empty);

            string volumePath = m_backup.GetSnapshotProperties(m_volumes[root]).SnapshotDeviceObject;

            if (!volumePath.EndsWith(SLASH) && !localPath.StartsWith(SLASH))
                localPath = localPath.Insert(0, SLASH);
            localPath = localPath.Insert(0, volumePath);

            return localPath;
        }

        #endregion

        #region ISnapshotService Members

        /// <summary>
        /// Enumerates all files and folders in the shadow copy
        /// </summary>
        /// <param name="filter">The filter to apply when evaluating files and folders</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        public void EnumerateFilesAndFolders(Core.FilenameFilter filter, Core.Utility.EnumerationCallbackDelegate callback)
        {
            foreach (string s in m_sourcepaths)
                Core.Utility.EnumerateFileSystemEntries(s, filter, callback, this.ListFolders, this.ListFiles);
        }

        /// <summary>
        /// Gets the last write time of a given file
        /// </summary>
        /// <param name="file">The full path to the file in non-shadow format</param>
        /// <returns>The last write time of the file</returns>
        public DateTime GetLastWriteTime(string file)
        {
            return Alphaleonis.Win32.Filesystem.File.GetLastWriteTime(GetSnapshotPath(file));
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-shadow format</param>
        /// <returns>An open filestream that can be read</returns>
        public System.IO.Stream OpenRead(string file)
        {
            return Alphaleonis.Win32.Filesystem.File.OpenRead(GetSnapshotPath(file));
        }

        #endregion

        #region IDisposable Members

        /// <summary>
        /// Cleans up any resources and closes the backup set
        /// </summary>
        public void Dispose()
        {
            try 
            {
                if (m_backup != null)
                    using (IVssAsync async = m_backup.BackupComplete())
                        async.Wait();
            }
            catch { }

            try
            {
                if (m_backup != null)
                    foreach (Guid g in m_volumes.Values)
                        try { m_backup.DeleteSnapshot(g, false); }
                        catch { }
            }
            catch { }

            if (m_backup != null)
            {
                m_backup.Dispose();
                m_backup = null;
            }
            
        }

        #endregion

    }
}
