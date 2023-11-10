export interface ProgressEvent {
  BackupID: string;
  TaskID: number;
  BackendAction: string;
  BackendPath: string;
  BackendFileSize: number;
  BackendFileProgress: number;
  BackendSpeed: number;
  BackendIsBlocking: boolean;

  CurrentFilename: string;
  CurrentFilesize: number;
  CurrentFileoffset: number;
  CurrentFilecomplete: boolean;

  Phase: string;
  OverallProgress: number;
  ProcessedFileCount: number;
  ProcessedFileSize: number;
  TotalFileCount: number;
  TotalFileSize: number;
  StillCounting: boolean;
}

export interface TaskInfo {
  // Task ID
  Item1: number;
  // Backup ID
  Item2: string | null;
}

export interface ScheduleEntry {
  // Backup ID
  Item1: string;
  // Date
  Item2: string;
}

export interface ServerStatus {
  lastEventId: number;
  lastDataUpdateId: number;
  lastNotificationUpdateId: number;
  estimatedPauseEnd: Date;
  pauseTimeRemain: number;
  activeTask: TaskInfo | null;
  programState: any | null;
  lastErrorMessage: string | null;
  connectionState: string;
  xsfrerror: boolean;
  connectionAttemptTimer: number;
  failedConnectionAttempts: number;
  lastPgEvent: ProgressEvent | null;
  updaterState: string;
  updatedVersion: string | null;
  updateReady: boolean;
  updateDownloadProgress: number;
  proposedSchedule: ScheduleEntry[];
  schedulerQueueIds: TaskInfo[];
}
