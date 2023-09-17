import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { catchError } from 'rxjs';
import { EMPTY } from 'rxjs';
import { AddOrUpdateBackupData } from './backup';
import { BackupService } from './services/backup.service';
import { DialogService } from './services/dialog.service';

export const backupResolver: ResolveFn<AddOrUpdateBackupData> = (route, state) => {
  const backupService = inject(BackupService);
  const dialog = inject(DialogService);
  const backupId = route.paramMap.get('backupId');
  if (backupId == null) {
    return EMPTY;
  }
  return backupService.getBackup(backupId).pipe(
    catchError(err => {
      dialog.connectionError($localize`Failed to load backup: `, err);
      return EMPTY;
    }));
};
