import { HttpClient } from '@angular/common/http';
import { Inject, Injectable, InjectionToken } from '@angular/core';
import { map, Observable, ReplaySubject } from 'rxjs';

export const DEFAULT_OPENSTACK_SERVER = new InjectionToken<string>("Default openstack server", {
  providedIn: 'root', factory: () => 'https://identity.api.rackspacecloud.com/'
});
export const DEFAULT_OPENSTACK_VERSION = new InjectionToken<string>("Default openstack version", {
  providedIn: 'root', factory: () => 'v2'
});

@Injectable({
  providedIn: 'root'
})
export class OpenstackService {

  private openstackProviders$?: ReplaySubject<Record<string, string | null>>;
  private openstackVersions$?: ReplaySubject<Record<string, string | null>>;

  constructor(private client: HttpClient) { }

  getProviders(): Observable<Record<string, string | null>> {
    if (this.openstackProviders$ == null) {
      this.openstackProviders$ = new ReplaySubject(1);
      let formData = new FormData();
      formData.set('openstack-config', 'Providers');
      this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/openstack-getconfig', formData).pipe(
        map(v => v.Result)
      ).subscribe(this.openstackProviders$);
    }
    return this.openstackProviders$.asObservable();
  }

  getVersions(): Observable<Record<string, string | null>> {
    if (this.openstackVersions$ == null) {
      this.openstackVersions$ = new ReplaySubject(1);
      let formData = new FormData();
      formData.set('openstack-config', 'Versions');
      this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/openstack-getconfig', formData).pipe(
        map(v => v.Result)
      ).subscribe(this.openstackVersions$);
    }
    return this.openstackVersions$.asObservable();
  }
}
