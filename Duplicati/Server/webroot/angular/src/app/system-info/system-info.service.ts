import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { SystemState } from './system-state';

@Injectable({
  providedIn: 'root'
})
export class SystemInfoService {
  private backendgroups: any = {
    std: {
      'ftp': null,
      'ssh': null,
      'webdav': null,
      'openstack': 'OpenStack Object Storage / Swift',
      's3': 'S3 Compatible',
      'aftp': 'FTP (Alternative)'
    },
    local: { 'file': null },
    prop: {
      'e2': null,
      's3': null,
      'azure': null,
      'googledrive': null,
      'onedrive': null,
      'onedrivev2': null,
      'sharepoint': null,
      'msgroup': null,
      'cloudfiles': null,
      'gcs': null,
      'openstack': null,
      'hubic': null,
      'b2': null,
      'mega': null,
      'idrive': null,
      'box': null,
      'od4b': null,
      'mssp': null,
      'dropbox': null,
      'sia': null,
      'storj': null,
      'tardigrade': null,
      'jottacloud': null,
      'rclone': null,
      'cos': null
    }
  };
  private GroupTypes: string[] = ['Local storage', 'Standard protocols', 'Proprietary', 'Others'];

  constructor(private http: HttpClient) { }

  getState(): Observable<SystemState> {
    return this.http.get<SystemState>('/systeminfo').pipe(map(state => { this.loadTexts(state); return state; }));
  }

  private loadTexts(state: SystemState): void {
    state.backendgroups = this.backendgroups;
    state.GroupTypes = this.GroupTypes;
  }
}
