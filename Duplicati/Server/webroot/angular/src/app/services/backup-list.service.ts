import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { AddOrUpdateBackupData } from '../backup';

@Injectable({
  providedIn: 'root'
})
export class BackupListService {

  constructor() { }

  public getBackups(): Observable<AddOrUpdateBackupData[]> {
    let mockBackup: AddOrUpdateBackupData = {
      IsUnencryptedOrPassphraseStored: false,
      Schedule: null,
      Backup: {
        ID: 'id',
        Name: 'backup name',
        Description: 'what',
        Tags: [],
        TargetURL: 'asdf',
        DBPath: 'path',
        Sources: [],
        Settings: [],
        Filters: [],
        Metadata: new Map<string, string>([['NextScheduledRun', 'now']]),
        IsTemporary: false
      }
    };
    return of([mockBackup]);
  }
}
