import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, combineLatest, concatAll, concatMap, distinctUntilChanged, filter, first, from, last, map, Observable, of, ReplaySubject, Subscription, takeUntil, takeWhile, tap, withLatestFrom, zip } from 'rxjs';
import { BackupLogEntry, LiveLogEntry, RemoteLogEntry, ServerLogEntry } from './log-entry';

@Injectable({
  providedIn: 'root'
})
export class LogService {

  constructor(private client: HttpClient) { }

  private getLogPage<LogEntry>(url: string, pageSize: number, offset?: number): Observable<LogEntry[]> {
    let params: { [p: string]: string | number } = { 'pagesize': pageSize };
    if (offset != null) {
      params['offset'] = offset;
    }
    return this.client.get<LogEntry[]>(url, {
      params: params
    });
  }

  // Load log page by page whenever nextPage emits
  private getLog<LogEntry>(pageSize: number, nextPage: Observable<void>,
    getPage: (offset?: number) => Observable<LogEntry[]>,
    getOffset: (e: LogEntry) => number): Observable<LogEntry[]> {
    let lastOffset: number | undefined;
    return nextPage.pipe(
      // Convert nextPage trigger to last loaded item
      map(() => lastOffset),
      // Only load any given offset once
      distinctUntilChanged(),
      map(offset => getPage(offset)),
      concatAll(),
      // Complete when last page is reached
      takeWhile(logs => logs.length === pageSize, true),
      tap(logs => {
        if (logs.length > 0) {
          lastOffset = getOffset(logs[logs.length - 1]);
        }
      }),
    );
  }

  getServerLogPage(pageSize: number, offsetTimestamp?: number): Observable<ServerLogEntry[]> {
    return this.getLogPage<ServerLogEntry>('/logdata/log', pageSize, offsetTimestamp);
  }

  getServerLog(pageSize: number, nextPage: Observable<void>): Observable<ServerLogEntry[]> {
    return this.getLog(pageSize, nextPage,
      (offset) => this.getServerLogPage(pageSize, offset),
      entry => entry.Timestamp);
  }

  getRemoteLogPage(backupId: string, pageSize: number, offsetId?: number): Observable<RemoteLogEntry[]> {
    return this.getLogPage<RemoteLogEntry>(`/backup/${backupId}/remotelog`, pageSize, offsetId);
  }

  getRemoteLog(backupId: string, pageSize: number, nextPage: Observable<void>): Observable<RemoteLogEntry[]> {
    return this.getLog(pageSize, nextPage,
      (offset) => this.getRemoteLogPage(backupId, pageSize, offset),
      entry => entry.ID);
  }

  getBackupLogPage(backupId: string, pageSize: number, offsetId?: number): Observable<BackupLogEntry[]> {
    return this.getLogPage<BackupLogEntry>(`/backup/${backupId}/log`, pageSize, offsetId).pipe(
      map(logs => {
        // Parse json if possible
        for (let entry of logs) {
          try {
            entry.Result = JSON.parse(entry.Message);
            entry.Formatted = JSON.stringify(entry.Result, null, 2);
          } catch (err) {
            // Ignore parse errors
          }
        }
        return logs;
      })
    );
  }

  getBackupLog(backupId: string, pageSize: number, nextPage: Observable<void>): Observable<BackupLogEntry[]> {
    return this.getLog(pageSize, nextPage,
      (offset) => this.getBackupLogPage(backupId, pageSize, offset),
      entry => entry.ID);
  }

  getLiveLog(pagesize: number, logLevel: Observable<string>): Observable<LiveLogEntry> {
    let liveRefreshId = new BehaviorSubject<number>(0);
    return logLevel.pipe(
      filter(l => l != ''),
      withLatestFrom(liveRefreshId),
      map(data => {
        return this.client.get<LiveLogEntry[]>('/logdata/poll', {
          params: {
            'level': data[0],
            'id': data[1],
            'pagesize': pagesize
          }
        });
      }),
      concatAll(),
      tap(logs => {
        let max = liveRefreshId.value;
        for (let l of logs) {
          max = Math.max(max, l.ID);
        }
        if (max != liveRefreshId.value) {
          liveRefreshId.next(max);
        }
      }),
      map(logs => logs.reverse()),
      // Flatten to individual entries
      concatMap(logs => from(logs))
    );
  }
}
