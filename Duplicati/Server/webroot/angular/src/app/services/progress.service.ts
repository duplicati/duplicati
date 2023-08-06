import { Injectable } from '@angular/core';
import { map, Observable, of } from 'rxjs';
import { merge, startWith } from 'rxjs/operators';
import { ConvertService } from './convert.service';
import { ServerStatus } from './server-status';
import { ServerStatusService } from './server-status.service';

@Injectable({
  providedIn: 'root'
})
export class ProgressService {

  private progressStateTexts = new Map<string, string | null>([
    ['Backup_Begin', 'Starting backup …'],
    ['Backup_PreBackupVerify', 'Verifying backend data …'],
    ['Backup_PostBackupTest', 'Verifying remote data …'],
    ['Backup_PreviousBackupFinalize', 'Completing previous backup …'],
    ['Backup_ProcessingFiles', null],
    ['Backup_Finalize', 'Completing backup …'],
    ['Backup_WaitForUpload', 'Waiting for upload to finish …'],
    ['Backup_Delete', 'Deleting unwanted files …'],
    ['Backup_Compact', 'Compacting remote data ...'],
    ['Backup_VerificationUpload', 'Uploading verification file …'],
    ['Backup_PostBackupVerify', 'Verifying backend data …'],
    ['Backup_Complete', 'Backup complete!'],
    ['Restore_Begin', 'Starting restore …'],
    ['Restore_RecreateDatabase', 'Rebuilding local database …'],
    ['Restore_PreRestoreVerify', 'Verifying remote data …'],
    ['Restore_CreateFileList', 'Building list of files to restore …'],
    ['Restore_CreateTargetFolders', 'Creating target folders …'],
    ['Restore_ScanForExistingFiles', 'Scanning existing files …'],
    ['Restore_ScanForLocalBlocks', 'Scanning for local blocks …'],
    ['Restore_PatchWithLocalBlocks', 'Patching files with local blocks …'],
    ['Restore_DownloadingRemoteFiles', 'Downloading files …'],
    ['Restore_PostRestoreVerify', 'Verifying restored files …'],
    ['Restore_Complete', 'Restore complete!'],
    ['Recreate_Running', 'Recreating database …'],
    ['Vacuum_Running', 'Vacuuming database …'],
    ['Repair_Running', 'Repairing database …'],
    ['Verify_Running', 'Verifying files …'],
    ['BugReport_Running', 'Creating bug report …'],
    ['Delete_Listing', 'Listing remote files …'],
    ['Delete_Deleting', 'Deleting remote files …'],
    ['PurgeFiles_Begin,', 'Listing remote files for purge …'],
    ['PurgeFiles_Process,', 'Purging files …'],
    ['PurgeFiles_Compact', 'Compacting remote data …'],
    ['PurgeFiles_Complete', 'Purging files complete!'],
    ['Error', 'Error!']
  ]);

  constructor(private serverStatus: ServerStatusService,
    private convert: ConvertService) { }

  getProgressStatusText(): Observable<string> {
    // Begin with undefined so a value is always returned immediately
    return this.serverStatus.getStatus().pipe(
      startWith(undefined),
      map(s => this.getStatusText(s))
    );
  }

  getProgressStatus(): Observable<number> {
    // Begin with undefined so a value is always returned immediately
    return this.serverStatus.getStatus().pipe(
      startWith(undefined),
      map(s => this.getProgress(s))
    );
  }

  private getProgress(s: ServerStatus | undefined): number {
    let pg = -1;
    if (s == null) {
      return pg;
    }
    if (s.lastPgEvent != null && s.activeTask != null) {
      const phase = s.lastPgEvent.Phase || '';
      if (phase == 'Backup_ProcessingFiles' || phase == 'Restore_DownloadingRemoteFiles') {
        if (s.lastPgEvent.StillCounting) {
          pg = 0;
        } else {
          const unaccountedbytes = s.lastPgEvent.CurrentFilecomplete ? 0 : s.lastPgEvent.CurrentFileoffset;
          pg = (s.lastPgEvent.ProcessedFileSize + unaccountedbytes) / s.lastPgEvent.TotalFileSize;

          if (s.lastPgEvent.ProcessedFileCount == 0) {
            pg = 0;
          } else if (pg >= 0.9) {
            pg = 0.9;
          }
        }
      } else if (phase == 'Backup_Finalize' || phase == 'Backup_WaitForUpload') {
        pg = 0.9;
      } else if (phase == 'Backup_Delete' || phase == 'Backup_Compact') {
        pg = 0.95;
      } else if (phase == 'Backup_VerificationUpload' || phase == 'Backup_PostBackupVerify') {
        pg = 0.98;
      } else if (phase == 'Backup_Complete' || phase == 'Backup_WaitForUpload') {
        pg = 1;
      } else if (s.lastPgEvent.OverallProgress > 0) {
        pg = s.lastPgEvent.OverallProgress;
      }
    }
    return pg;
  }

  private getStatusText(s: ServerStatus | undefined): string {
    let text = 'Running …';
    if (s == null) {
      return text;
    }

    if (s.lastPgEvent != null && s.activeTask != null) {
      const phase = s.lastPgEvent.Phase || '';
      text = this.progressStateTexts.get(phase) || phase;

      if (phase === 'Backup_ProcessingFiles' || phase === 'Restore_DownloadingRemoteFiles') {
        if (s.lastPgEvent.StillCounting) {
          text = `Counting (${s.lastPgEvent.TotalFileCount} files found, ${this.convert.formatSizeString(s.lastPgEvent.TotalFileSize)})`;
        } else {
          const unaccountedbytes = s.lastPgEvent.CurrentFilecomplete ? 0 : s.lastPgEvent.CurrentFileoffset;
          const filesleft = s.lastPgEvent.TotalFileSize - s.lastPgEvent.ProcessedFileCount;
          const sizeleft = s.lastPgEvent.TotalFileSize - s.lastPgEvent.ProcessedFileSize - unaccountedbytes;

          // If we have a speed append it
          const speed_txt = s.lastPgEvent.BackendSpeed < 0 ? '' : `at ${this.convert.formatSizeString(s.lastPgEvent.BackendSpeed)}/s`;

          const restoring_text = phase == 'Restore_DownloadingRemoteFiles' ? 'Restoring' : '';

          text = restoring_text + `${filesleft} files (${this.convert.formatSizeString(sizeleft)}) to go ${speed_txt}`;
        }
      }
      return text;
    }

    return text;
  }
}
