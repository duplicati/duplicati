import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class RemoteOperationService {

  constructor(private client: HttpClient) { }

  locateDbUri(uri: string): Observable<{ Exists: string, Path: string | null }> {
    return this.client.post<{ Exists: string, Path: string | null }>('/remoteoperation/dbpath', uri);
  }
}
