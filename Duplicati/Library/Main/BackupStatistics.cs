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
    internal class BackupStatistics : CommunicationStatistics
    {
        private long m_deletedFiles;
        private long m_deletedFolders;
        private long m_modifiedFiles;
        private long m_examinedFiles;
        private long m_openedFiles;
        private long m_addedFiles;
        private long m_sizeOfModifiedFiles;
        private long m_sizeOfAddedFiles;
        private long m_sizeOfExaminedFiles;
        private long m_unprocessedFiles;
        private long m_addedFolders;
        private long m_tooLargeFiles;
        private long m_filesWithError;
        private long m_modifiedFolders;
        private long m_modifiedSymlinks;
        private long m_addedSymlinks;
        private long m_deletedSymlinks;

        private bool m_partialBackup = false;

        public BackupStatistics(OperationMode operationMode)
            : base(operationMode)
        {
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

        public long OpenedFiles
        {
            get { return m_openedFiles; }
            set { m_openedFiles = value; }
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

        public long ModifiedFolders
        {
            get { return m_modifiedFolders; }
            set { m_modifiedFolders = value; }
        }

        public long AddedSymlinks
        {
            get { return m_addedSymlinks; }
            set { m_addedSymlinks = value; }
        }
        
        public long ModifiedSymlinks
        {
            get { return m_modifiedSymlinks; }
            set { m_modifiedSymlinks = value; }
        }
        
        public long DeletedSymlinks
        {
            get { return m_deletedSymlinks; }
            set { m_deletedSymlinks = value; }
        }


        public long FilesTooLarge
        {
            get { return m_tooLargeFiles; }
            set { m_tooLargeFiles = value; }
        }

        public long FilesWithError
        {
            get { return m_filesWithError; }
            set { m_filesWithError = value; }
        }

        public bool PartialBackup
        {
            get { return m_partialBackup; }
            set { m_partialBackup = true; }
        }

        public override string ToString()
        {
            //TODO: Figure out how to translate this without breaking the output parser
            StringBuilder sb = new StringBuilder();
            sb.Append(base.ToString());
            sb.Append("DeletedFiles    : " + this.DeletedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("DeletedFolders  : " + this.DeletedFolders.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("DeletedSymlinks : " + this.DeletedSymlinks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("ModifiedFiles   : " + this.ModifiedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("ModifiedFolders : " + this.ModifiedFolders.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("ModifiedSymlinks: " + this.ModifiedSymlinks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("AddedFiles      : " + this.AddedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("AddedFolders    : " + this.AddedFolders.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("AddedSymlinks   : " + this.AddedSymlinks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("ExaminedFiles   : " + this.ExaminedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("OpenedFiles     : " + this.OpenedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("SizeOfModified  : " + this.SizeOfModifiedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("SizeOfAdded     : " + this.SizeOfAddedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("SizeOfExamined  : " + this.SizeOfExaminedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("Unprocessed     : " + this.UnprocessedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("TooLargeFiles   : " + this.FilesTooLarge.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            sb.Append("FilesWithError  : " + this.FilesWithError.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n");
            if (m_partialBackup)
                sb.Append("PartialBackup   : true" + "\r\n");
            
            return sb.ToString();
        }
    }
}
