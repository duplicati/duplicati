import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, ResolveFn, Router, RouterStateSnapshot } from '@angular/router';
import { catchError, EMPTY, map } from 'rxjs';
import { AddOrUpdateBackupData } from '../backup';
import { BackupDefaultsService } from '../services/backup-defaults.service';
import { BackupService } from '../services/backup.service';
import { DialogService } from '../services/dialog.service';
import { ImportService } from '../services/import.service';

export const editBackupResolver: ResolveFn<AddOrUpdateBackupData> = (route: ActivatedRouteSnapshot, state: RouterStateSnapshot) => {
  const router = inject(Router);
  const dialog = inject(DialogService);
  const backupService = inject(BackupService);
  const importService = inject(ImportService);
  const backupDefaults = inject(BackupDefaultsService);
  const backupId = route.paramMap.get('backupId');

  if (backupId == null) {
    const isImport = route.data['import'] == true;
    return backupDefaults.getBackupDefaults().pipe(
      map(b => {
        if (isImport) {
          const importConfig = importService.getImportData();
          Object.assign(b, importConfig);
        }
        return b;
      }),
      catchError(err => {
        router.navigate(['/']);
        dialog.connectionError(err);
        return EMPTY;
      }));
  } else {
    return backupService.getBackup(backupId).pipe(
      catchError(err => {
        router.navigate(['/']);
        dialog.connectionError($localize`Failed to load backup: `, err);
        return EMPTY;
      }));
  }
};
