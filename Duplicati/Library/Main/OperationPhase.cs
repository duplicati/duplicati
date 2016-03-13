//  Copyright (C) 2013, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;

namespace Duplicati.Library.Main
{
    public enum OperationPhase
    {
        Backup_Begin,
        Backup_PreBackupVerify,
        Backup_PostBackupTest,
        Backup_PreviousBackupFinalize,
        Backup_ProcessingFiles,
        Backup_Finalize,
        Backup_WaitForUpload,
        Backup_Delete,
        Backup_Compact,
        Backup_VerificationUpload,
        Backup_PostBackupVerify,
        Backup_Complete,
        
        Restore_Begin,
        Restore_RecreateDatabase,
        Restore_PreRestoreVerify,
        Restore_CreateFileList,
        Restore_CreateTargetFolders,
        Restore_ScanForExistingFiles,
        Restore_ScanForLocalBlocks,
        Restore_PatchWithLocalBlocks,
        Restore_DownloadingRemoteFiles,
        Restore_PostRestoreVerify,
        Restore_Complete,

        Recreate_Running,
        Verify_Running,

        BugReport_Running,

        Error
    }
}

