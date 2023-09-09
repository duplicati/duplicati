import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class UpdateService {

  constructor(private client: HttpClient) { }

  startUpdateDownload(): Observable<void> {
    return this.client.post<void>('/updates/install', '');
  }
  startUpdateActivate(): Observable<void> {
    return this.client.post<void>('/updates/activate', '');
  }
  checkForUpdates(): Observable<void> {
    return this.client.post<void>('/updates/check', '');
  }

  getUpdateChangelog(): Observable<{ Version: string, Changelog: string }> {
    return this.client.get<{ Version: string, Changelog: string }>('/changelog?from-update=true');
  }
}
