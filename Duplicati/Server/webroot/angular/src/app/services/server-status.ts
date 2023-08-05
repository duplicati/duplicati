export interface ServerStatus {
  lastEventId: number;
  lastDataUpdateId: number;
  lastNotificationUpdateId: number;
  estimatedPauseEnd: Date;
  activeTask: string | null;
  programState: any | null;
  lastErrorMessage: string | null;
  connectionState: string;
  xsfrerror: boolean;
  connectionAttemptTimer: number;
  failedConnectionAttempts: number;
  lastPgEvent: any | null;
  updaterState: string;
  updatedVersion: any | null;
  updateReady: boolean;
  updateDownloadProgress: number;
  proposedSchedule: any[];
  schedulerQueueIds: any[];
}
