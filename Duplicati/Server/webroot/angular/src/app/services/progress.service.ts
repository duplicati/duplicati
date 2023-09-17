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
    ['Backup_Begin', $localize`Starting backup …`],
    ['Backup_PreBackupVerify', $localize`Verifying backend data …`],
    ['Backup_PostBackupTest', $localize`Verifying remote data …`],
    ['Backup_PreviousBackupFinalize', $localize`Completing previous backup …`],
    ['Backup_ProcessingFiles', null],
    ['Backup_Finalize', $localize`Completing backup …`],
    ['Backup_WaitForUpload', $localize`Waiting for upload to finish …`],
    ['Backup_Delete', $localize`Deleting unwanted files …`],
    ['Backup_Compact', $localize`Compacting remote data ...`],
    ['Backup_VerificationUpload', $localize`Uploading verification file …`],
    ['Backup_PostBackupVerify', $localize`Verifying backend data …`],
    ['Backup_Complete', $localize`Backup complete!`],
    ['Restore_Begin', $localize`Starting restore …`],
    ['Restore_RecreateDatabase', $localize`Rebuilding local database …`],
    ['Restore_PreRestoreVerify', $localize`Verifying remote data …`],
    ['Restore_CreateFileList', $localize`Building list of files to restore …`],
    ['Restore_CreateTargetFolders', $localize`Creating target folders …`],
    ['Restore_ScanForExistingFiles', $localize`Scanning existing files …`],
    ['Restore_ScanForLocalBlocks', $localize`Scanning for local blocks …`],
    ['Restore_PatchWithLocalBlocks', $localize`Patching files with local blocks …`],
    ['Restore_DownloadingRemoteFiles', $localize`Downloading files …`],
    ['Restore_PostRestoreVerify', $localize`Verifying restored files …`],
    ['Restore_Complete', $localize`Restore complete!`],
    ['Recreate_Running', $localize`Recreating database …`],
    ['Vacuum_Running', $localize`Vacuuming database …`],
    ['Repair_Running', $localize`Repairing database …`],
    ['Verify_Running', $localize`Verifying files …`],
    ['BugReport_Running', $localize`Creating bug report …`],
    ['Delete_Listing', $localize`Listing remote files …`],
    ['Delete_Deleting', $localize`Deleting remote files …`],
    ['PurgeFiles_Begin,', $localize`Listing remote files for purge …`],
    ['PurgeFiles_Process,', $localize`Purging files …`],
    ['PurgeFiles_Compact', $localize`Compacting remote data …`],
    ['PurgeFiles_Complete', $localize`Purging files complete!`],
    ['Error', $localize`Error!`]
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
    let text = $localize`Running …`;
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
          const filesleft = e.TotalFileCount - e.ProcessedFileCount;
          const sizeleft = e.TotalFileSize - e.ProcessedFileSize - unaccountedbytes;

          // If we have a speed append it
          const speed_txt = e.BackendSpeed < 0 ? '' : $localize`at ${this.convert.formatSizeString(e.BackendSpeed)}/s`;

          const restoring_text = phase == 'Restore_DownloadingRemoteFiles' ? $localize`Restoring` : '';

          text = restoring_text + $localize`${filesleft} files (${this.convert.formatSizeString(sizeleft)}) to go ${speed_txt}`;
        }
      }
      return text;
    }

    return text;
  }
}
