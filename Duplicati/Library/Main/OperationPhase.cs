// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

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
        Vacuum_Running,
        Verify_Running,

        BugReport_Running,

        Delete_Listing,
        Delete_Deleting,

        PurgeFiles_Begin,
        PurgeFiles_Process,
        PurgeFiles_Compact,
        PurgeFiles_Complete,

        Error
    }
}

