import { HttpClient } from '@angular/common/http';
import { InjectionToken } from '@angular/core';
import { Inject } from '@angular/core';
import { Injectable } from '@angular/core';
import { map, Observable, ReplaySubject } from 'rxjs';
import { DialogService } from '../../services/dialog.service';

export const S3_CLIENT_OPTIONS = new InjectionToken<({ name: string, label: string })[]>("S3 client options", {
  providedIn: 'root', factory: () => [
    { name: 'aws', label: 'Amazon AWS SDK' },
    { name: 'minio', label: 'Minio SDK' },
  ]
});

export const DEFAULT_S3_SERVER = new InjectionToken<string>("Default S3 server", {
  providedIn: 'root', factory: () => 's3.amazonaws.com'
});

@Injectable({
  providedIn: 'root'
})
export class S3Service {

  private providers$?: ReplaySubject<Record<string, string | null>>;
  private regions$?: ReplaySubject<Record<string, string | null>>;
  private storageClasses$?: ReplaySubject<Record<string, string | null>>;

  get clientOptions(): ({ name: string, label: string })[] {
    return this.s3ClientOptions;
  }

  constructor(@Inject(S3_CLIENT_OPTIONS) private s3ClientOptions: ({ name: string, label: string })[],
    private dialog: DialogService,
    private client: HttpClient) { }

  getStorageClasses(): Observable<Record<string, string | null>> {
    if (this.storageClasses$ == null) {
      this.storageClasses$ = new ReplaySubject(1);
      let formData = new FormData();
      formData.set('s3-config', 'StorageClasses');
      this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/s3-getconfig', formData).pipe(
        map(v => v.Result)
      ).subscribe(this.storageClasses$);
    }
    return this.storageClasses$.asObservable();
  }
  getProviders(): Observable<Record<string, string | null>> {
    if (this.providers$ == null) {
      this.providers$ = new ReplaySubject(1);
      let formData = new FormData();
      formData.set('s3-config', 'Providers');
      this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/s3-getconfig', formData).pipe(
        map(v => v.Result)
      ).subscribe(this.providers$);
    }
    return this.providers$.asObservable();
  }
  getRegions(): Observable<Record<string, string | null>> {
    if (this.regions$ == null) {
      this.regions$ = new ReplaySubject(1);
      let formData = new FormData();
      formData.set('s3-config', 'Regions');
      this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/s3-getconfig', formData).pipe(
        map(v => v.Result)
      ).subscribe(this.regions$);
    }
    return this.regions$.asObservable();
  }

  createIAMUser(path: string, username: string, password: string): Observable<{ accessid: string, secretkey: string, username: string }> {
    let formData = new FormData();
    formData.set('s3-operation', 'CreateIAMUser');
    formData.set('s3-path', path);
    formData.set('s3-username', username);
    formData.set('s3-password', password);
    return this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/s3-iamconfig', formData).pipe(
      map(v => {
        if (!v.Result['accessid'] || !v.Result['secretkey'] || !v.Result['username']) {
          throw new Error('Unexpected result');
        }
        return { accessid: v.Result['accessid'], secretkey: v.Result['secretkey'], username: v.Result['username'] };
      })
    );
  }
  getIAMPolicy(path: string): Observable<{ doc: string }> {
    let formData = new FormData();
    formData.set('s3-operation', 'GetPolicyDoc');
    formData.set('s3-path', path);
    return this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/s3-iamconfig', formData).pipe(
      map(v => {
        if (!v.Result['doc']) {
          throw new Error('Unexpected result');
        }
        return { doc: v.Result['doc'] };
      })
    );
  }

  canGeneratePolicy(server: string | undefined) {
    return server == 's3.amazonaws.com';
  }

  testPermissions(username: string, password: string): Observable<{ isroot: string, ex: string, error: string } | { isroot: string, arn: string, id: string, name: string }> {
    let formData = new FormData();
    formData.set('s3-operation', 'CanCreateUser');
    formData.set('s3-username', username);
    formData.set('s3-password', password);

    return this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/s3-iamconfig', formData).pipe(
      map(v => {
        if (v.Result['ex'] && v.Result['isroot']) {
          return { isroot: v.Result['isroot'], ex: v.Result['ex'], error: v.Result['error'] ?? '' };
        } else if (v.Result['isroot'] != null && v.Result['arn'] != null && v.Result['id'] != null && v.Result['name'] != null) {
          return { isroot: v.Result['isroot'], arn: v.Result['arn'], id: v.Result['id'], name: v.Result['name'] };
        } else {
          throw new Error('Unexpected result');
        }
      }));
  }
}
