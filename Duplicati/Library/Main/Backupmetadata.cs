using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// This class holds all metadata gathered during any remote backend operation
    /// </summary>
    public class Backupmetadata
    {
        private long? m_total_size;
        private long? m_full_backup_count;
        private long? m_total_volume_count;
        private long? m_total_file_count;

        private DateTime? m_last_backup_date;
        private long? m_last_backup_size;

        private long? m_alien_file_count;
        private long? m_alien_file_size;

        private long? m_source_file_size;
        private long? m_source_file_count;
        private long? m_source_folder_count;

        private long? m_total_backup_size;
        private long? m_total_backup_sets;

        private long? m_total_quota_space;
        private long? m_free_quota_space;
        private long? m_assigned_quota_space;

        public long TotalSize { get { return m_total_size ?? 0; } set { m_total_size = value; } }
        public long AlienFileSize { get { return m_alien_file_size ?? 0; } set { m_alien_file_size = value; } }
        public long AlienFileCount { get { return m_alien_file_count ?? 0; } set { m_alien_file_count = value; } }
        public long FullBackupCount { get { return m_full_backup_count ?? 0; } set { m_full_backup_count = value; } }
        public long TotalVolumeCount { get { return m_total_volume_count ?? 0; } set { m_total_volume_count = value; } }
        public long TotalFileCount { get { return m_total_file_count ?? 0; } set { m_total_file_count = value; } }
        public DateTime LastBackupDate { get { return m_last_backup_date ?? new DateTime(0); } set { m_last_backup_date = value; } }
        public long LastBackupSize { get { return m_last_backup_size ?? 0; } set { m_last_backup_size = value; } }
        public long SourceFileSize { get { return m_source_file_size ?? 0; } set { m_source_file_size = value; } }
        public long SourceFileCount { get { return m_source_file_count ?? 0; } set { m_source_file_count = value; } }
        public long SourceFolderCount { get { return m_source_folder_count ?? 0; } set { m_source_folder_count = value; } }
        public long TotalBackupSize { get { return m_total_backup_size ?? 0; } set { m_total_backup_size = value; } }
        public long TotalBackupSets { get { return m_total_backup_sets ?? 0; } set { m_total_backup_sets = value; } }
        public long TotalQuotaSpace { get { return m_total_quota_space ?? 0; } set { m_total_quota_space = value; } }
        public long FreeQuotaSpace { get { return m_free_quota_space ?? 0; } set { m_free_quota_space = value; } }
        public long AssignedQuotaSpace { get { return m_assigned_quota_space ?? 0; } set { m_assigned_quota_space = value; } }

        public IDictionary<string, string> AsReport()
        {
            Dictionary<string, string> result = new Dictionary<string,string>();
            foreach (System.Reflection.FieldInfo fi in this.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.NonPublic))
            {
                if (fi.Name.StartsWith("m_") && (fi.FieldType == typeof(long?) || fi.FieldType == typeof(int?) || fi.FieldType == typeof(string) || fi.FieldType == typeof(DateTime?)))
                {
                    object v = fi.GetValue(this);
                    if (v != null)
                    {
                        string reportname = fi.Name.Substring("m_".Length).Replace('_', '-');
                        if (fi.FieldType == typeof(DateTime?))
                            result[reportname] = ((DateTime?)v).Value.ToUniversalTime().ToString("u");
                        else
                            result[reportname] = v.ToString();
                    }
                }
            }

            return result;
        }

        public void RemoveCurrentBackupData()
        {
            m_last_backup_date = null;
            m_last_backup_size = null;
        }
    }
}
