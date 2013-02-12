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

namespace Duplicati.Library.Main
{
    internal class RestoreStatistics : CommunicationStatistics
    {
        private DateTime m_beginTime;
        private DateTime m_endTime;
        private long m_filesRestored;
        private long m_sizeOfRestoredFiles;
        private long m_foldersRestored;
        private long m_filesPatched;
        private long m_filesDeleted;
        private long m_foldersDeleted;

        public RestoreStatistics(DuplicatiOperationMode operationMode)
            : base(operationMode)
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

        public long FilesRestored
        {
            get { return m_filesRestored; }
            set { m_filesRestored = value; }
        }

        public long SizeOfRestoredFiles
        {
            get { return m_sizeOfRestoredFiles; }
            set { m_sizeOfRestoredFiles = value; }
        }

        public long FoldersRestored
        {
            get { return m_foldersRestored; }
            set { m_foldersRestored = value; }
        }

        public long FoldersDeleted
        {
            get { return m_foldersDeleted; }
            set { m_foldersDeleted = value; }
        }

        public long FilesPatched
        {
            get { return m_filesPatched; }
            set { m_filesPatched = value; }
        }

        public long FilesDeleted
        {
            get { return m_filesDeleted; }
            set { m_filesDeleted = value; }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("BeginTime       : " + this.BeginTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("EndTime         : " + this.EndTime.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Duration        : " + this.Duration.ToString());
            sb.AppendLine("Files restored  : " + this.FilesRestored.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Files patched   : " + this.FilesPatched.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Files deleted   : " + this.FilesDeleted.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Restored size   : " + this.SizeOfRestoredFiles.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Folders created : " + this.FoldersRestored.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Folders deleted : " + this.FoldersDeleted.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(base.ToString());
            return sb.ToString();
        }
    }
}
