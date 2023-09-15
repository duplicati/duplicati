import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { take } from 'rxjs';
import { map } from 'rxjs';
import { BehaviorSubject } from 'rxjs';
import { Observable, ReplaySubject } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class GcsService {

  private gcsLocation$?: ReplaySubject<Record<string, string | null>>;
  private gcsStorageClasses$?: ReplaySubject<Record<string, string | null>>;

  constructor(private client: HttpClient) { }

  getLocations(): Observable<Record<string, string | null>> {
    if (this.gcsLocation$ == null) {
      this.gcsLocation$ = new ReplaySubject(1);
      let formData = new FormData();
      formData.set('gcs-config', 'Locations');
      this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/gcs-getconfig', formData).pipe(
        map(v => v.Result)
      ).subscribe(this.gcsLocation$);
    }
    return this.gcsLocation$.asObservable();
  }

  getStorageClasses(): Observable<Record<string, string | null>> {
    if (this.gcsStorageClasses$ == null) {
      this.gcsStorageClasses$ = new ReplaySubject(1);
      let formData = new FormData();
      formData.set('gcs-config', 'StorageClasses');
      this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/gcs-getconfig', formData).pipe(
        map(v => v.Result)
      ).subscribe(this.gcsStorageClasses$);
    }
    return this.gcsStorageClasses$.asObservable();
  }
}
