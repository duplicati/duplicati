import { Injectable } from '@angular/core';
import { ServerStatus } from './server-status';


@Injectable({
  providedIn: 'root'
})
export class ServerStatusService {

  longpolltime = 5 * 60 * 1000;

  status: ServerStatus = {
    lastEventId: -1,
    lastDataUpdateId: -1,
    lastNotificationUpdateId: -1,
    estimatedPauseEnd: new Date("0001-01-01T00:00:00"),
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

  constructor() { }

  resume(): void { }
  pause(): void { }
  reconnect(): void { }
  callWhenTaskCompletes(taskid: string, callback: any) { }

}
