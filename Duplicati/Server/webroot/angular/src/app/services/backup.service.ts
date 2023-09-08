import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable, tap } from 'rxjs';
import { AddOrUpdateBackupData } from '../backup';
import { DialogService } from './dialog.service';
import { ServerStatusService } from './server-status.service';

@Injectable({
  providedIn: 'root'
})
export class BackupService {

  constructor(private client: HttpClient,
    private serverStatus: ServerStatusService,
    private dialogService: DialogService) { }

  doRun(id: string): void {
    this.client.post('/backup/' + id + '/run', '').pipe(tap(() => {
      if (this.serverStatus.status.programState === 'Paused') {
        this.dialogService.dialog('Server paused', 'Server is currently paused, do you want to resume now?', ['No', 'Yes'], (idx) => {
          if (idx == 1) {
            this.serverStatus.resume();
          }
        });
      }
    })).subscribe();
  }
  doCompact(id: string): void {
    this.client.post('/backup/' + id + '/compact', '').subscribe();
  }
  doCreateBugReport(id: string): void {
    this.client.post('/backup/' + id + '/createreport', '').subscribe();
  }
  doVerifyRemote(id: string): void {
    this.client.post('/backup/' + id + '/verify', '').subscribe();
  }
  isActive(id: string): Observable<boolean> {
    return this.client.get<{ Status: string, Active: boolean }>('/backup/' + id + '/isactive').pipe(
      map(resp => resp.Status === 'OK' && resp.Active)
    );
  }
  doRepair(id: string): void {
    this.client.post('/backup/' + id + '/repair', '').subscribe();
  }
  putBackup(id: string, b: AddOrUpdateBackupData): Observable<void> {
    return this.client.put<void>('/backup/' + id, b);
  }
  getBackup(id: string): Observable<AddOrUpdateBackupData> {
    return this.client.get<{ success: boolean, data: AddOrUpdateBackupData }>('/backup/' + id).pipe(
      map(resp => {
        if (!resp.success) {
          throw new Error('Failed to get backup');
        }
        return resp.data;
      }));
  }
}
