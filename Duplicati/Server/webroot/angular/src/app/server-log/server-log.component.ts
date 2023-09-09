import { Component, EventEmitter } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { from, fromEvent, interval, map, Observable, Subject, Subscription } from 'rxjs';
import { DialogService } from '../services/dialog.service';
import { LiveLogEntry, ServerLogEntry } from '../services/log-entry';
import { LogService } from '../services/log.service';
import { ServerStatusService } from '../services/server-status.service';
import { SystemInfoService } from '../system-info/system-info.service';

@Component({
  selector: 'app-server-log',
  templateUrl: './server-log.component.html',
  styleUrls: ['./server-log.component.less']
})
export class ServerLogComponent {

  page: string = 'stored';
  LiveLogLevel: string = '';
  LiveRefreshID: number = 0;
  LogData?: ServerLogEntry[];
  LogDataComplete: boolean = false;
  LiveData?: LiveLogEntry[];
  LoadingData: boolean = false;
  LogLevels: string[] = [];

  private logPageSize = 100;

  private subscription?: Subscription;
  private logPageSubscription?: Subscription;
  private liveLogSubscription?: Subscription;
  private nextPage$ = new Subject<void>();
  private liveLevel$ = new Subject<string>();
  private liveRefreshTimer?: Subscription;

  constructor(private logService: LogService,
    private serverStatus: ServerStatusService,
    private systemInfo: SystemInfoService,
    private dialog: DialogService,
    private route: ActivatedRoute) { }

  ngOnInit() {
    this.subscription = new Subscription();
    this.subscription.add(this.route.paramMap.subscribe(params => {
      this.setPage(params.get('page') || 'stored');
    }));
    this.subscription.add(this.systemInfo.getState().subscribe(i => {
      this.LogLevels = i.LogLevels;
    }));

    this.logPageSubscription = this.logService.getServerLog(this.logPageSize, this.nextPage$).subscribe({
      next: logs => {
        if (this.LogData == null) {
          this.LogData = logs;
        } else {
          this.LogData.push(...logs);
        }
      },
      error: this.dialog.connectionError('Failed to connect: '),
      complete: () => this.LogDataComplete = true
    });
    this.nextPage$.next();
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
    this.logPageSubscription?.unsubscribe();
  }

  setPage(p: string): void {
    if (this.page != p) {
      this.page = p;
      this.updateLivePoll();
    }
  }

  setLiveLogLevel(l: string): void {
    if (l !== this.LiveLogLevel) {
      this.LiveLogLevel = l;
      this.updateLivePoll();
    }
  }

  updateLivePoll(): void {
    if (this.page != 'live' || this.LiveLogLevel == '') {
      this.liveLevel$?.complete();
      this.liveRefreshTimer?.unsubscribe();
      this.liveRefreshTimer = undefined;
      return;
    }
    if (this.liveRefreshTimer === undefined) {
      // Setup refresh timer
      this.liveLevel$ = new Subject<string>();
      this.liveRefreshTimer = interval(3000).pipe(map(() => this.LiveLogLevel)).subscribe(this.liveLevel$);
      this.liveLogSubscription = this.logService.getLiveLog(this.logPageSize, this.liveLevel$).subscribe(l => {
        if (this.LiveData == null) {
          this.LiveData = [];
        }
        this.LiveData.unshift(l);
        this.LiveData.length = Math.min(300, this.LiveData.length);
      });
    }
    this.liveLevel$.next(this.LiveLogLevel);
  }

  loadMoreStoredData(): void {
    this.nextPage$.next();
  }
}
