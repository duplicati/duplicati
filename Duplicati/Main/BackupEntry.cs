using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Main
{
    internal class BackupEntry
    {
        private Duplicati.Backend.FileEntry m_fileentry;
        private DateTime m_time;
        private List<BackupEntry> m_incrementals;
        private bool m_isContent;
        private bool m_isFull;

        public string Filename { get { return m_fileentry.Name; } }
        public Backend.FileEntry FileEntry { get { return m_fileentry; } }
        public DateTime Time { get { return m_time; } }
        public bool IsContent { get { return m_isContent; } }
        public bool IsFull { get { return m_isFull; } }
        public List<BackupEntry> Incrementals { get { return m_incrementals; } }

        public BackupEntry(Backend.FileEntry fe, DateTime time, bool isContent, bool isFull)
        {
            m_fileentry = fe;
            m_time = time;
            m_isContent = isContent;
            m_isFull = isFull;
            m_incrementals = new List<BackupEntry>();
        }
    }

    internal class Sorter : IComparer<BackupEntry>
    {
        #region IComparer<BackupEntry> Members

        public int Compare(BackupEntry x, BackupEntry y)
        {
            return x.Time.CompareTo(y.Time);
        }

        #endregion
    }

}
