import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
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
    // TODO: add error message
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
  deleteDatabase(backupId: string): Observable<void> {
    return this.client.post<void>(`/backup/${backupId}/deletedb`, '');
  }
  updateDatabase(backupId: string, path: string, move?: boolean): Observable<void> {
    const target = move ? 'movedb' : 'updatedb';
    let formData = new FormData();
    formData.append('path', path);
    return this.client.post<void>(`/backup/${backupId}/${target}`, formData);
  }

  isDbUsedElsewhere(backupId: string, path: string): Observable<boolean> {
    return this.client.get<{ inuse: boolean }>(`/backup/${backupId}/isdbusedelsewhere`, { params: { path: path } }).pipe(
      map(resp => resp.inuse)
    );
  }

  // Returns task id of report task
  startSizeReport(backupId: string): Observable<string> {
    return this.client.post<{ Status: string, ID: string }>(`/backup/${backupId}/report-remote-size`, '').pipe(
      map(resp => resp.ID)
    );
  }
  // Returns task id
  deleteBackup(backupId: string, deleteLocalDb: boolean, deleteRemoteFiles: boolean, captchaToken?: string, captchaAnswer?: string): Observable<string> {
    let params = new HttpParams();
    params.append('delete-local-db', deleteLocalDb);
    params.append('delete-remote-files', deleteRemoteFiles);
    if (captchaToken != null) {
      params.append('captcha-token', captchaToken);
    }
    if (captchaAnswer != null) {
      params.append('captcha-answer', captchaAnswer);
    }
    return this.client.delete<{ Status: string, ID: string }>(`/backup/${backupId}`, { params: params }).pipe(
      map(resp => resp.ID)
    );
  }
}
