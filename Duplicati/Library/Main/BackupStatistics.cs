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

namespace Duplicati.Library.Main
{
    internal class BackupStatistics : CommunicationStatistics
    {
        private DateTime m_beginTime;
        private DateTime m_endTime;
        private long m_deletedFiles;
        private long m_deletedFolders;
        private long m_modifiedFiles;
        private long m_examinedFiles;
        private long m_addedFiles;
        private long m_sizeOfModifiedFiles;
        private long m_sizeOfAddedFiles;
        private long m_sizeOfExaminedFiles;
        private long m_unprocessedFiles;
        private long m_addedFolders;

        public BackupStatistics()
        {
            m_beginTime = m_endTime = DateTime.Now;
        }

        public DateTime BeginTime
        {
            get { return m_beginTime; }
            set { m_beginTime = value; }
        }

        public DateTime EndTime
        {
            get { return m_endTime; }
            set { m_endTime = value; }
        }

        public TimeSpan Duration
        {
            get { return m_endTime - m_beginTime; }
        }

        public long DeletedFiles
        {
            get { return m_deletedFiles; }
            set { m_deletedFiles = value; }
        }

        public long DeletedFolders
        {
            get { return m_deletedFolders; }
            set { m_deletedFolders = value; }
        }

        public long ModifiedFiles
        {
            get { return m_modifiedFiles; }
            set { m_modifiedFiles = value; }
        }

        public long AddedFiles
        {
            get { return m_addedFiles; }
            set { m_addedFiles = value; }
        }

        public long ExaminedFiles
        {
            get { return m_examinedFiles; }
            set { m_examinedFiles = value; }
        }

        public long SizeOfModifiedFiles
        {
            get { return m_sizeOfModifiedFiles; }
            set { m_sizeOfModifiedFiles = value; }
        }

        public long SizeOfAddedFiles
        {
            get { return m_sizeOfAddedFiles; }
            set { m_sizeOfAddedFiles = value; }
        }

        public long SizeOfExaminedFiles
        {
            get { return m_sizeOfExaminedFiles; }
            set { m_sizeOfExaminedFiles = value; }
        }

        public long UnprocessedFiles
        {
            get { return m_unprocessedFiles; }
            set { m_unprocessedFiles = value; }
        }

        public long AddedFolders
        {
            get { return m_addedFolders; }
            set { m_addedFolders = value; }
        }


        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("BeginTime       : " + this.BeginTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("EndTime         : " + this.EndTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("Duration        : " + this.Duration.ToString() + "\r\n");
            sb.Append("DeletedFiles    : " + this.DeletedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("DeletedFolders  : " + this.DeletedFolders.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("ModifiedFiles   : " + this.ModifiedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("AddedFiles      : " + this.ModifiedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("AddedFolders    : " + this.AddedFolders.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("ExaminedFiles   : " + this.ExaminedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("SizeOfModified  : " + this.SizeOfModifiedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("SizeOfAdded     : " + this.SizeOfAddedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("SizeOfExamined  : " + this.SizeOfExaminedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("Unprocessed     : " + this.UnprocessedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            
            sb.Append(base.ToString());
            return sb.ToString();
        }
    }
}
