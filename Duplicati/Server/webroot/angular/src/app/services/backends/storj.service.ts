import { HttpClient } from '@angular/common/http';
import { Inject } from '@angular/core';
import { InjectionToken } from '@angular/core';
import { Injectable } from '@angular/core';
import { map, Observable, ReplaySubject } from 'rxjs';

export const DEFAULT_STORJ_SATELLITE = new InjectionToken<string>('Default storj satellite', {
  providedIn: 'root', factory: () => 'us1.storj.io:7777'
});

@Injectable({
  providedIn: 'root'
})
export class StorjService {

  private satellites$?: ReplaySubject<Record<string, string | null>>;
  private authMethods$?: ReplaySubject<Record<string, string | null>>;

  get defaultStorjSatellite(): string {
    return this.defaultSatellite;
  }

  constructor(@Inject(DEFAULT_STORJ_SATELLITE) private defaultSatellite: string,
    private client: HttpClient) { }

  getSatellites(): Observable<Record<string, string | null>> {
    if (this.satellites$ == null) {
      this.satellites$ = new ReplaySubject(1);
      let formData = new FormData();
      formData.set('storj-config', 'Satellites');
      this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/storj-getconfig', formData).pipe(
        map(v => v.Result)
      ).subscribe(this.satellites$);
    }
    return this.satellites$.asObservable();
  }
  getAuthMethods(): Observable<Record<string, string | null>> {
    if (this.authMethods$ == null) {
      this.authMethods$ = new ReplaySubject(1);
      let formData = new FormData();
      formData.set('storj-config', 'AuthenticationMethods');
      this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/storj-getconfig', formData).pipe(
        map(v => v.Result)
      ).subscribe(this.authMethods$);
    }
    return this.authMethods$.asObservable();
  }

}
