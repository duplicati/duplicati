import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable, ReplaySubject, share, shareReplay, Subscription, tap } from 'rxjs';
import { GroupedModuleDescription, ModuleDescription, SystemInfo } from './system-info';

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

  private state?: SystemInfo;
  private state$?: ReplaySubject<SystemInfo>;
  private stateRequest$?: Subscription;

  constructor(private http: HttpClient) { }

  getState(): Observable<SystemInfo> {
    if (this.state$ === undefined) {
      this.state$ = new ReplaySubject<SystemInfo>(1);
      this.reload();
    }
    return this.state$.asObservable();
  }

  private reloadBackendConfig(state: SystemInfo): void {
    if (state.BackendModules == null) {
      return;
    }

    state.GroupedBackendModules = [];

    let pushWithType = (m: ModuleDescription, order: number, alternate?: string | null): boolean => {
      let copy = structuredClone(m) as GroupedModuleDescription;
      copy.GroupType = state.GroupTypes[order];
      if (alternate != null) {
        copy.DisplayName = alternate;
      }
      copy.OrderKey = order;
      state.GroupedBackendModules!.push(copy);
      return true;
    }

    for (let m of state.BackendModules) {
      let used = false;

      if (state.backendgroups.local[m.Key] !== undefined) {
        let res = pushWithType(m, 0, state.backendgroups.local[m.Key]);
        used = used || res;
      }
      if (state.backendgroups.std[m.Key] !== undefined) {
        let res = pushWithType(m, 1, state.backendgroups.std[m.Key]);
        used = used || res;
      }
      if (state.backendgroups.prop[m.Key] !== undefined) {
        let res = pushWithType(m, 2, state.backendgroups.prop[m.Key]);
        used = used || res;
      }

      if (!used) {
        pushWithType(m, 3);
      }
    }
  }

  private loadTexts(state: SystemInfo): void {
    state.backendgroups = this.backendgroups;
    state.GroupTypes = this.GroupTypes;

    this.reloadBackendConfig(state);

    if (state.GroupedBackendModules != null) {
      for (let m of state.GroupedBackendModules) {
        m.GroupType = this.GroupTypes[m.OrderKey];
      }
    }
  }

  reload(): void {
    if (this.stateRequest$) {
      this.stateRequest$?.unsubscribe();
    }
    if (this.state$ === undefined) {
      this.state$ = new ReplaySubject<SystemInfo>(1);
    }
    this.stateRequest$ = this.http.get<SystemInfo>('/systeminfo').pipe(map(state => { this.loadTexts(state); return state; }))
      .subscribe(s => {
        this.state = s;
        this.state$!.next(s);
      });
  }

  suppressDonationMessages(suppress: boolean): Observable<void> {
    return this.http.post<void>(suppress ? '/systeminfo/suppressdonationmessages' : '/systeminfo/showdonationmessages', '').pipe(
      tap(() => {
        if (this.state) {
          this.state.SuppressDonationMessages = suppress;
          this.state$?.next(this.state);
        }
      })
    );
  }
}
