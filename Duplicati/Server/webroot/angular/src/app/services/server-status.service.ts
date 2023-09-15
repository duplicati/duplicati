import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { catchError, map, Observable, ReplaySubject, Subject, Subscription, timeout } from 'rxjs';
import { ConvertService } from './convert.service';
import { ProgressEvent, ScheduleEntry, ServerStatus, TaskInfo } from './server-status';


@Injectable({
  providedIn: 'root'
})
export class ServerStatusService {

  longpolltime = 5 * 60 * 1000;

  private _status: ServerStatus = {
    lastEventId: -1,
    lastDataUpdateId: -1,
    lastNotificationUpdateId: -1,
    estimatedPauseEnd: new Date("0001-01-01T00:00:00"),
    pauseTimeRemain: 0,
    activeTask: null,
    programState: null,
    lastErrorMessage: null,
    connectionState: 'connected',
    xsfrerror: false,
    connectionAttemptTimer: 0,
    failedConnectionAttempts: 0,
    lastPgEvent: null,
    updaterState: 'Waiting',
    updatedVersion: null,
    updateReady: false,
    updateDownloadProgress: 0,
    proposedSchedule: [],
    schedulerQueueIds: []
  };
  get status(): ServerStatus {
    return this._status;
  }

  private status$ = new ReplaySubject<ServerStatus>(1);
  statusRequest?: Subscription;
  progressEvent$ = new Subject<ProgressEvent>();

  private waitingForTask = new Map<number, (() => void)[]>();
  private longPollRetryTimer?: number;
  private progressPollRunning = false;
  private progressPollTimer?: number;
  private progressPollWait = 2000;
  private updatePauseTimer?: number;
  private pollRunning = false;

  constructor(private client: HttpClient,
    private convert: ConvertService) {
  }

  getStatus(): Observable<ServerStatus> {
    if (!this.pollRunning) {
      this.longpoll(true);
      this.pollRunning = true;
    }
    return this.status$.asObservable();
  }
  getProposedSchedule(): Observable<ScheduleEntry[]> {
    return this.getStatus().pipe(map(s => s.proposedSchedule));
  }

  resume(): void {
    this.client.post('/serverstate/resume', '').subscribe();
  }
  pause(duration?: string): void {
    if (duration !== undefined) {
      let formData = new FormData();
      formData.set('duration', duration);
      this.client.post('/serverstate/pause', formData).subscribe();
    } else {
      this.client.post('/serverstate/pause', '').subscribe();
    }
  }
  reconnect(): void { this.longpoll(true); }
  callWhenTaskCompletes(taskid: number, callback: () => void) {
    let callbacks = this.waitingForTask.get(taskid);
    if (callbacks === undefined) {
      this.waitingForTask.set(taskid, [callback]);
    } else {
      callbacks.push(callback);
    }
  }

  longpoll(fastcall: boolean): void {
    if (this.longPollRetryTimer !== undefined) {
      clearInterval(this.longPollRetryTimer);
      this.longPollRetryTimer = undefined;
    }

    if (this.status.connectionState !== 'connected') {
      this.status.connectionState = 'connecting';
      this.status$.next(this.status);
    }
    if (this.statusRequest) {
      this.statusRequest.unsubscribe();
    }
    this.statusRequest = this.client.get<any>('/serverstate/', {
      params: {
        lasteventid: this.status.lastEventId,
        longpoll: (!fastcall && this.status.lastEventId > 0),
        duration: (this.longpolltime - 1000) / 1000 + 's'
      }
    }).pipe(timeout({ each: this.status.lastEventId > 0 ? this.longpolltime : 5000 }),
      catchError(err => this.handleError(err)))
      .subscribe(v => {
        let oldEventId = this.status.lastEventId;
        let anyChanged = this.updateStatus(v);

        if (this.status.proposedSchedule.length !== v['ProposedSchedule'].length
          || !this.status.proposedSchedule.every((val, index) => val === v['ProposedSchedule'][index])) {
          this.status.proposedSchedule.length = 0;
          this.status.proposedSchedule.push(...v['ProposedSchedule']);
          anyChanged = true;
        }

        if (this.status.schedulerQueueIds.length !== v['SchedulerQueueIds'].length
          || !this.status.schedulerQueueIds.every((val, index) => val === v['SchedulerQueueIds'][index])) {
          this.status.schedulerQueueIds.length = 0;
          this.status.schedulerQueueIds.push(...v['SchedulerQueueIds']);
          anyChanged = true;
        }

        // Clear error indicators
        this.status.failedConnectionAttempts = 0;
        this.status.xsfrerror = false;

        if (this.status.connectionState !== 'connected') {
          this.status.connectionState = 'connected';
          anyChanged = true;

          // Reload page, server restarted
          if (oldEventId > this.status.lastEventId)
            location.reload();
        }

        if (this.pauseTimerUpdater(true)) {
          anyChanged = true;
        }

        if (anyChanged) {
          this.status$.next(this.status);
        }

        if (this.status.activeTask !== null) {
          this.startUpdateProgressPoll();
        }

        this.longpoll(false);
      });

  }

  private pauseTimerUpdater(skipNotify: boolean): boolean {
    const prev = this.status.pauseTimeRemain;

    this.status.pauseTimeRemain = Math.max(0, this.status.estimatedPauseEnd.getTime() - new Date().getTime());
    if (this.status.pauseTimeRemain > 0 && this.updatePauseTimer === undefined) {
      this.updatePauseTimer = setInterval(() => this.pauseTimerUpdater(false), 500);
    } else if (this.status.pauseTimeRemain <= 0 && this.updatePauseTimer !== undefined) {
      clearInterval(this.updatePauseTimer);
      this.updatePauseTimer = undefined;
    }

    if (prev !== this.status.pauseTimeRemain && !skipNotify) {
      this.status$.next(this.status);
    }

    return prev !== this.status.pauseTimeRemain;
  }

  private startUpdateProgressPoll() {
    if (this.progressPollRunning) {
      return;
    }

    if (this.status.activeTask == null) {
      if (this.progressPollTimer !== undefined) {
        clearTimeout(this.progressPollTimer);
        this.progressPollTimer = undefined;
      }
      this.status.lastPgEvent = null;
    } else {
      this.progressPollRunning = true;
      if (this.progressPollTimer !== undefined) {
        clearTimeout(this.progressPollTimer);
        this.progressPollTimer = undefined;
      }

      this.client.get<ProgressEvent>('/progressstate')
        .subscribe(p => {
          this.status.lastPgEvent = p;
          this.progressEvent$.next(p);

          this.progressPollRunning = false;
          this.progressPollTimer = setTimeout(() => this.startUpdateProgressPoll(), this.progressPollWait);
        }, () => {
          this.progressPollRunning = false;
          this.progressPollTimer = setTimeout(() => this.startUpdateProgressPoll(), this.progressPollWait);
        });
    }
  }

  private countdownForReLongPoll(callback: () => void): void {
    if (this.longPollRetryTimer !== undefined) {
      clearInterval(this.longPollRetryTimer);
      this.longPollRetryTimer = undefined;
    }

    const retryAt = new Date(new Date().getTime() + (this.status.xsfrerror ? 500 : 15000));
    this.status.connectionAttemptTimer = retryAt.getTime() - new Date().getTime();
    this.status$.next(this.status);

    this.longPollRetryTimer = setInterval(() => {
      this.status.connectionAttemptTimer = retryAt.getTime() - new Date().getTime();
      if (this.status.connectionAttemptTimer <= 0) {
        callback();
      } else {
        this.status$.next(this.status);
      }
    }, 1000);
  }

  private handleError(err: HttpErrorResponse) {
    const oldxsfrstate = this.status.xsfrerror;
    this.status.failedConnectionAttempts++;
    this.status.xsfrerror = err.status == 400 && err.statusText.toLowerCase().indexOf('xsrf') >= 0;

    // First failure, we ignore
    if (this.status.connectionState === 'connected' && this.status.failedConnectionAttempts === 1) {
      // Try again
      this.longpoll(true);
    } else {
      this.status.connectionState = 'disconnected';

      // If we got a new XSRF token this time, quickly retry
      if (this.status.xsfrerror && !oldxsfrstate) {
        this.longpoll(true);
      } else {
        // Otherwise, start countdown to next try
        this.countdownForReLongPoll(() => this.longpoll(true));
      }
    }

    return new Observable<never>();
  }

  private activeTaskChanged(newTask: TaskInfo | null): void {
    const newTaskId = newTask?.Item1;
    const lastTaskId = this.status.activeTask?.Item1;

    if (lastTaskId != null && newTaskId != lastTaskId && this.waitingForTask.has(lastTaskId)) {
      for (let callback of this.waitingForTask.get(lastTaskId)!) {
        callback();
      }
      this.waitingForTask.delete(lastTaskId);
    }
  }

  private updateStatus(v: any): boolean {
    let anyChanged = false;
    if (v['LastEventID'] !== this.status.lastEventId) {
      this.status.lastEventId = v['LastEventID'];
      anyChanged = true;
    }
    if (v['LastDataUpdateID'] !== this.status.lastDataUpdateId) {
      this.status.lastDataUpdateId = v['LastDataUpdateID'];
      anyChanged = true;
    }
    if (v['LastNotificationUpdateID'] !== this.status.lastNotificationUpdateId) {
      this.status.lastNotificationUpdateId = v['LastNotificationUpdateID'];
      anyChanged = true;
    }
    if (v['ActiveTask'] !== this.status.activeTask) {
      this.activeTaskChanged(v['ActiveTask']);
      this.status.activeTask = v['ActiveTask'];
      anyChanged = true;
    }
    if (v['ProgramState'] !== this.status.programState) {
      this.status.programState = v['ProgramState'];
      anyChanged = true;
    }
    if (v['EstimatedPauseEnd'] !== this.status.estimatedPauseEnd) {
      this.status.estimatedPauseEnd = this.convert.parseDate(v['EstimatedPauseEnd']);
      anyChanged = true;
    }
    if (v['UpdaterState'] !== this.status.updaterState) {
      this.status.updaterState = v['UpdaterState'];
      anyChanged = true;
    }
    if (v['UpdateReady'] !== this.status.updateReady) {
      this.status.updateReady = v['UpdateReady'];
      anyChanged = true;
    }
    if (v['UpdatedVersion'] !== this.status.updatedVersion) {
      this.status.updatedVersion = v['UpdatedVersion'];
      anyChanged = true;
    }
    if (v['UpdateDownloadProgress'] !== this.status.updateDownloadProgress) {
      this.status.updateDownloadProgress = v['UpdateDownloadProgress'];
      anyChanged = true;
    }

    return anyChanged;
  }
}
