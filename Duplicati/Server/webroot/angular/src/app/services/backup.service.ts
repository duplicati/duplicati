import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable, tap } from 'rxjs';
import { AddOrUpdateBackupData, ListFile, Fileset } from '../backup';
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
  getFilesets(id: string, includeMetadata?: boolean, fromRemoteOnly?: boolean): Observable<Fileset[]> {
    let params: Record<string, string | number | boolean> = {};
    if (includeMetadata !== undefined) {
      params['include-metadata'] = includeMetadata;
    }
    if (fromRemoteOnly !== undefined) {
      params['from-remote-only'] = fromRemoteOnly;
    }
    return this.client.get<Fileset[]>(`/backup/${id}/filesets`, { params: params });
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
  deleteBackup(backupId: string, deleteLocalDb: boolean, deleteRemoteFiles: boolean, captchaToken?: string, captchaAnswer?: string): Observable<number> {
    let params = new HttpParams();
    params.append('delete-local-db', deleteLocalDb);
    params.append('delete-remote-files', deleteRemoteFiles);
    if (captchaToken != null) {
      params.append('captcha-token', captchaToken);
    }
    if (captchaAnswer != null) {
      params.append('captcha-answer', captchaAnswer);
    }
    return this.client.delete<{ Status: string, ID: number }>(`/backup/${backupId}`, { params: params }).pipe(
      map(resp => resp.ID)
    );
  }
  exportCommandLine(backupId: string, exportPasswords: boolean): Observable<string> {
    return this.client.get<{ Command: string }>(`/backup/${backupId}/export`, {
      params: {
        'cmdline': true, 'export-passwords': exportPasswords
      }
    }).pipe(
      map(resp => resp.Command)
    );
  }
  exportCommandLineArgs(backupId: string, exportPasswords: boolean): Observable<{ Backend: string, Arguments: string[], Options: string[] }> {
    return this.client.get<{ Backend: string, Arguments: string[], Options: string[] }>(`/backup/${backupId}/export`, {
      params: {
        'argsonly': true,
        'export-passwords': exportPasswords
      }
    });
  }
  searchFiles(backupId: string, searchFilter: string | null, timestamp: string,
    params?: { prefixOnly?: boolean, folderContents?: boolean, exactMatch?: boolean }):
    Observable<{ Filesets: Fileset[], Files: ListFile[], [k: string]: string | Fileset[] | ListFile[] }> {
    let filterString = searchFilter || '';
    if (!params?.exactMatch) {
      filterString = searchFilter != null ? `*${searchFilter}*` : '*';
    }
    let requestParams: Record<string, string | number | boolean> = {
      'prefix-only': params?.prefixOnly ?? false,
      'time': timestamp
    };
    if (searchFilter != null) {
      requestParams['filter'] = params?.exactMatch ? `@${searchFilter}` : filterString!;
    }
    if (params?.folderContents !== undefined) {
      requestParams['folder-contents'] = params.folderContents;
    }
    return this.client.get<{ Filesets: Fileset[], Files: ListFile[], [k: string]: string | Fileset[] | ListFile[] }>(
      `/backup/${backupId}/files/${encodeURIComponent(filterString)}`, {
      params: requestParams
    });
  }
  restore(backupId: string, params?: any): Observable<number> {
    let formData = new FormData();
    for (const k in params) {
      formData.set(k, params[k]?.toString() || '');
    }
    return this.client.post<{ TaskID: number }>(`/backup/${backupId}/restore`, formData).pipe(
      map(v => v.TaskID)
    );
  }
  repair(backupId: string, params?: any): Observable<number> {
    let formData = new FormData();
    for (const k in params) {
      formData.set(k, params[k]);
    }
    return this.client.post<{ Status: string, ID: number }>(`/backup/${backupId}/repair`, formData).pipe(
      map(v => v.ID)
    );
  }
  repairUpdateTemporary(backupId: string, timestamp: string): Observable<number> {
    let formData = new FormData();
    formData.set('only-paths', 'true');
    formData.set('time', timestamp);
    return this.client.post<{ Status: string, ID: number }>(`/backup/${backupId}/repairupdate`, formData).pipe(
      map(v => v.ID)
    );
  }
  copyToTemp(backupId: string): Observable<string> {
    return this.client.post<{ Status: string, ID: string }>(`/backup/${backupId}/copytotemp`, '').pipe(
      map(v => v.ID));
  }
}
