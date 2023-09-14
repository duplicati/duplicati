import { Injectable } from '@angular/core';
import { map, Observable, of } from 'rxjs';
import { distinctUntilChanged, filter, merge, startWith, tap } from 'rxjs/operators';
import { ConvertService } from './convert.service';
import { ServerStatus, ProgressEvent, TaskInfo } from './server-status';
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

  private activeTask: TaskInfo | null = null;

  getProgressEvents(): Observable<ProgressEvent | null> {
    const statusUpdates = this.serverStatus.getStatus().pipe(
      tap(s => this.activeTask = s.activeTask),
      map(s => s.lastPgEvent));
    return this.serverStatus.progressEvent$.pipe(
      merge(statusUpdates));
  }

  getProgressStatusText(): Observable<string> {
    // Begin with undefined so a value is always returned immediately
    return this.getProgressEvents().pipe(
      startWith(null),
      map(e => this.getStatusText(e)),
      distinctUntilChanged());
  }

  getProgressStatus(): Observable<number> {
    // Begin with undefined so a value is always returned immediately
    return this.getProgressEvents().pipe(
      startWith(null),
      map(e => this.getProgress(e)),
      distinctUntilChanged());
  }

  private getProgress(e: ProgressEvent | null): number {
    let pg = -1;
    if (e == null) {
      return pg;
    }
    if (e != null && this.activeTask != null) {
      const phase = e.Phase || '';
      if (phase == 'Backup_ProcessingFiles' || phase == 'Restore_DownloadingRemoteFiles') {
        if (e.StillCounting) {
          pg = 0;
        } else {
          const unaccountedbytes = e.CurrentFilecomplete ? 0 : e.CurrentFileoffset;
          pg = (e.ProcessedFileSize + unaccountedbytes) / e.TotalFileSize;

          if (e.ProcessedFileCount == 0) {
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
      } else if (e.OverallProgress > 0) {
        pg = e.OverallProgress;
      }
    }
    return pg;
  }

  public getStatusText(e: ProgressEvent | null): string {
    let text = 'Running …';
    if (e == null) {
      return text;
    }

    if (e != null && this.activeTask != null) {
      const phase = e.Phase || '';
      text = this.progressStateTexts.get(phase) || phase;

      if (phase === 'Backup_ProcessingFiles' || phase === 'Restore_DownloadingRemoteFiles') {
        if (e.StillCounting) {
          text = `Counting (${e.TotalFileCount} files found, ${this.convert.formatSizeString(e.TotalFileSize)})`;
        } else {
          const unaccountedbytes = e.CurrentFilecomplete ? 0 : e.CurrentFileoffset;
          const filesleft = e.TotalFileSize - e.ProcessedFileCount;
          const sizeleft = e.TotalFileSize - e.ProcessedFileSize - unaccountedbytes;

          // If we have a speed append it
          const speed_txt = e.BackendSpeed < 0 ? '' : `at ${this.convert.formatSizeString(e.BackendSpeed)}/s`;

          const restoring_text = phase == 'Restore_DownloadingRemoteFiles' ? 'Restoring' : '';

          text = restoring_text + `${filesleft} files (${this.convert.formatSizeString(sizeleft)}) to go ${speed_txt}`;
        }
      }
      return text;
    }

    return text;
  }
}
