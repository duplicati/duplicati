using Alphaleonis.Win32.Vss;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Duplicati.Library.Snapshots
{
    public class MSSQLDB : IEquatable<MSSQLDB>
    {
        public string Name { get; }
        public string ID { get; }
        public List<string> DataPaths { get; }

        public MSSQLDB(string Name, string ID, List<string> DataPaths)
        {
            this.Name = Name;
            this.ID = ID;
            this.DataPaths = DataPaths;
        }

        bool IEquatable<MSSQLDB>.Equals(MSSQLDB other)
        {
            return ID.Equals(other.ID);
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            MSSQLDB db = obj as MSSQLDB;
            if (db != null)
            {
                return Equals(db);
            }
            else
            {
                return false;
            }
        }

        public static bool operator ==(MSSQLDB db1, MSSQLDB db2)
        {
            if (object.ReferenceEquals(db1, db2)) return true;
            if (object.ReferenceEquals(db1, null)) return false;
            if (object.ReferenceEquals(db2, null)) return false;

            return db1.Equals(db2);
        }

        public static bool operator !=(MSSQLDB db1, MSSQLDB db2)
        {
            if (object.ReferenceEquals(db1, db2)) return false;
            if (object.ReferenceEquals(db1, null)) return true;
            if (object.ReferenceEquals(db2, null)) return true;

            return !db1.Equals(db2);
        }
    }

    public class MSSQLUtility
    {
        /// <summary>
        /// The MS SQL VSS Writer Guid
        /// </summary>
        public static readonly Guid MSSQLWriterGuid = new Guid("a65faa63-5ea8-4ebc-9dbd-a0c4db26912a");
        /// <summary>
        /// MS SQL is supported only on Windows platform
        /// </summary>
        public bool IsMSSQLInstalled { get; }
        /// <summary>
        /// Enumerated MS SQL DBs
        /// </summary>
        public List<MSSQLDB> DBs { get { return m_DBs; } }
        private List<MSSQLDB> m_DBs;

        public MSSQLUtility()
        {
            m_DBs = new List<MSSQLDB>();

            if (!Utility.Utility.IsClientWindows)
            {
                IsMSSQLInstalled = false;
                return;
            }

            var arrInstalledInstances = (string [])Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SQL Server", "InstalledInstances", "");
            IsMSSQLInstalled = arrInstalledInstances == null ? false : arrInstalledInstances.Length > 0;

            if (!IsMSSQLInstalled)
                Logging.Log.WriteMessage("Cannot find any MS SQL Server instance. MS SQL Server is probably not installed.", Logging.LogMessageType.Information);
        }

        /// <summary>
        /// For all MS SQL databases it enumerate all associated paths using VSS data
        /// </summary>
        /// <returns>A collection of DBs and paths</returns>
        public void QueryDBsInfo()
        {
            if (!IsMSSQLInstalled)
                return;

            m_DBs.Clear();
            
            //Substitute for calling VssUtils.LoadImplementation(), as we have the dlls outside the GAC
            string alphadir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "alphavss");
            string alphadll = Path.Combine(alphadir, VssUtils.GetPlatformSpecificAssemblyShortName() + ".dll");
            IVssImplementation vss = (IVssImplementation)System.Reflection.Assembly.LoadFile(alphadll).CreateInstance("Alphaleonis.Win32.Vss.VssImplementation");

            using (var m_backup = vss.CreateVssBackupComponents())
            {
                m_backup.InitializeForBackup(null);
                m_backup.SetContext(VssSnapshotContext.Backup);
                m_backup.SetBackupState(false, true, VssBackupType.Full, false);
                m_backup.EnableWriterClasses(new Guid[] { MSSQLWriterGuid });

                try
                {
                    m_backup.GatherWriterMetadata();
                    var writerMetaData = m_backup.WriterMetadata.FirstOrDefault(o => o.WriterId.Equals(MSSQLWriterGuid));

                    if (writerMetaData == null)
                        throw new Exception("Microsoft SQL Server VSS Writer not found - cannot backup SQL databases.");

                    foreach (var component in writerMetaData.Components)
                    {
                        var paths = new List<string>();

                        foreach (var file in component.Files)
                            if (file.FileSpecification.Contains("*"))
                            {
                                if (Directory.Exists(Utility.Utility.AppendDirSeparator(file.Path)))
                                    paths.Add(Utility.Utility.AppendDirSeparator(file.Path));
                            }
                            else
                            {
                                if (File.Exists(Path.Combine(file.Path, file.FileSpecification)))
                                    paths.Add(Path.Combine(file.Path, file.FileSpecification));
                            }

                        m_DBs.Add(new MSSQLDB(component.ComponentName, component.LogicalPath + "\\" + component.ComponentName, paths.Distinct(Utility.Utility.ClientFilenameStringComparer).OrderBy(a => a).ToList()));
                    }
                }
                finally
                {
                    m_backup.FreeWriterMetadata();
                }
            }
        }
    }
}