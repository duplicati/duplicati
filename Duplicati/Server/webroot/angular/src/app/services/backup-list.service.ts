import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { AddOrUpdateBackupData } from '../backup';

@Injectable({
  providedIn: 'root'
})
export class BackupListService {

  constructor(private client: HttpClient) { }

  public getBackups(): Observable<AddOrUpdateBackupData[]> {
    return this.client.get<AddOrUpdateBackupData[]>('/backups');
  }
}
