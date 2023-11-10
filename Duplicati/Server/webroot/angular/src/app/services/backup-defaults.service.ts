import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { AddOrUpdateBackupData } from '../backup';

@Injectable({
  providedIn: 'root'
})
export class BackupDefaultsService {

  constructor(private client: HttpClient) { }

  getBackupDefaults(): Observable<AddOrUpdateBackupData> {
    return this.client.get<{ success: boolean, data: AddOrUpdateBackupData }>('/backupdefaults').pipe(
      map(resp => {
        if (!resp.success) {
          throw new Error('Failed to get backup');
        }
        return resp.data;
      }));
  }
}
