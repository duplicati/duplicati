import { inject } from '@angular/core';
import { ResolveFn, Router } from '@angular/router';
import { catchError, EMPTY, map, of, take } from 'rxjs';
import { AddOrUpdateBackupData, Fileset } from '../backup';
import { backupResolver } from '../backup.resolver';
import { BackupListService } from '../services/backup-list.service';
import { DialogService } from '../services/dialog.service';
import { RestoreService } from '../services/restore.service';

export interface RestoreData {
  isTemp: boolean,
  tempFilesets?: Fileset[],
  backup?: AddOrUpdateBackupData
}

export const restoreResolver: ResolveFn<RestoreData> = (route, state) => {
  const router = inject(Router);
  const dialog = inject(DialogService);
  const restoreService = inject(RestoreService);
  const backupList = inject(BackupListService);
  const backupId = route.paramMap.get('backupId');

  if (backupId == null) {
    return EMPTY;
  }

  const isTemp = parseInt(backupId) + '' != backupId;
  const tempFileset = restoreService.getTemporaryFileset(backupId);
  if (tempFileset != null) {
    // Pass in filelist through restoreService on direct restore to avoid another query
    return of({ isTemp: isTemp, tempFilesets: tempFileset });
  } else {
    // Have to use backupList, because normal backup does not return IsUnencryptedOrPassphraseStored
    return backupList.getBackupsLookup().pipe(take(1),
      map(backups => {
        const res = { isTemp: isTemp, backup: backups[backupId] };
        if (res.backup == null) {
          throw new Error('Invalid or missing backup id');
        }
        return res;
      }), catchError(err => {
        router.navigate(['/']);
        dialog.connectionError('Failed to load backup: ', err);
        return EMPTY;
      }));
  }
};
