import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { concatMap, map } from 'rxjs';
import { startWith } from 'rxjs';
import { interval, Observable } from 'rxjs';

export interface CommandLineOutput {
  Started: boolean;
  Items: string[];
  Finished: boolean;
  Count: number;
  Pagesize: number;
}

@Injectable({
  providedIn: 'root'
})
export class CommandlineService {

  constructor(private client: HttpClient) { }

  getSupportedCommands(): Observable<string[]> {
    return this.client.get<string[]>('/commandline');
  }

  getOutput(viewid: string, pagesize: number, offset: number): Observable<CommandLineOutput> {
    return this.client.get<CommandLineOutput>(`/commandline/${viewid}`, {
      params: {
        pagesize: pagesize,
        offset: offset
      }
    });
  }

  run(commandline: string[]): Observable<string> {
    return this.client.post<{ ID: string }>('/commandline', commandline).pipe(map(v => v.ID));
  }

  abort(viewid: string): Observable<void> {
    return this.client.post<{ Status: string }>(`/commandline/${viewid}/abort`, '').pipe(map(() => { }));
  }
}
