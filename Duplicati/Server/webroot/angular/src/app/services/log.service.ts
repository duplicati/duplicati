import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, combineLatest, concatAll, concatMap, distinctUntilChanged, filter, first, from, last, map, Observable, of, ReplaySubject, Subscription, takeUntil, takeWhile, tap, withLatestFrom, zip } from 'rxjs';
import { LiveLogEntry, ServerLogEntry } from './log-entry';

@Injectable({
  providedIn: 'root'
})
export class LogService {

  private liveLog$?: ReplaySubject<ServerLogEntry>;

  constructor(private client: HttpClient) { }

  getServerLogPage(pageSize: number, offsetTimestamp?: number): Observable<ServerLogEntry[]> {
    let params: { [p: string]: string | number } = { 'pageSize': pageSize };
    if (offsetTimestamp != null) {
      params['offset'] = offsetTimestamp;
    }
    return this.client.get<ServerLogEntry[]>('/logdata/log', {
      params: params
    });
  }

  getServerLog(pageSize: number, nextPage: Observable<void>): Observable<ServerLogEntry[]> {
    let last: number | undefined;
    return nextPage.pipe(
      // Convert nextPage trigger to last loaded item
      map(() => last),
      // Only load any given offset once
      distinctUntilChanged(),
      map(offset => this.getServerLogPage(pageSize, offset)),
      concatAll(),
      // Complete when last page is reached
      takeWhile(logs => logs.length === pageSize, true),
      tap(logs => {
        if (logs.length > 0) {
          last = logs[logs.length - 1].Timestamp;
        }
      }),
    );
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
