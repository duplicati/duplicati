import { Component } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { AddOrUpdateBackupData } from '../backup';
import { BackupListService } from '../services/backup-list.service';
import { ConvertService } from '../services/convert.service';
import { DialogCallback } from '../services/dialog-config';
import { DialogService } from '../services/dialog.service';
import { ProgressService } from '../services/progress.service';
import { ServerStatus } from '../services/server-status';
import { ServerStatusService } from '../services/server-status.service';
import { TaskService } from '../services/task.service';

@Component({
  selector: 'app-state',
  templateUrl: './state.component.html',
  styleUrls: ['./state.component.less']
})
export class StateComponent {

  activeTaskID?: number;
  activeBackup?: AddOrUpdateBackupData;
  nextTask?: AddOrUpdateBackupData;
  nextScheduledTask?: AddOrUpdateBackupData;
  nextScheduledTime?: Date;

  private backups?: Record<string, AddOrUpdateBackupData>;
  private state?: ServerStatus;
  private subscription?: Subscription;

  progress: number = -1;
  progressText: string = '';
  stopReqId?: number;
  programState?: string;


  constructor(private serverStatus: ServerStatusService,
    private backupList: BackupListService,
    private convert: ConvertService,
    private progressService: ProgressService,
    private taskService: TaskService,
    private dialog: DialogService) { }
  ngOnInit() {
    this.subscription = this.serverStatus.getStatus().subscribe(s => {
      this.state = s;
      this.updateActiveTask(s);
    });
    this.subscription.add(this.backupList.getBackupsLookup().subscribe(b => {
      this.backups = b;
      if (this.state) {
        this.updateActiveTask(this.state);
      }
    }));
    this.subscription.add(this.progressService.getProgressStatus().subscribe(v => this.progress = v));
    this.subscription.add(this.progressService.getProgressStatusText().subscribe(v => this.progressText = v));
  }
  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  private updateActiveTask(s: ServerStatus): void {
    if (this.backups === undefined) {
      return;
    }

    if (s.activeTask != null) {
      this.activeTaskID = s.activeTask.Item1;
      this.activeBackup = s.activeTask.Item2 != null ? this.backups[s.activeTask.Item2] : undefined;
    } else {
      this.activeTaskID = undefined;
      this.activeBackup = undefined;
    }

    if (s.schedulerQueueIds.length != 0 && s.schedulerQueueIds[0].Item2 != null) {
      this.nextTask = this.backups[s.schedulerQueueIds[0].Item2];
    } else {
      this.nextTask = undefined;
    }

    if (s.proposedSchedule.length != 0) {
      this.nextScheduledTask = this.backups[s.proposedSchedule[0].Item1];
      this.nextScheduledTime = this.convert.parseDate(s.proposedSchedule[0].Item2);
    } else {
      this.nextScheduledTask = undefined;
      this.nextScheduledTime = undefined;
    }
    this.programState = this.state?.programState;
  }

  sendResume() {
    this.serverStatus.resume();
  }

  stopDialog() {
    if (this.activeTaskID == null) {
      return;
    }

    const taskId = this.activeTaskID;
    const txt = this.state?.lastPgEvent == null ? '' : (this.state.lastPgEvent.Phase || '');

    const handleClick: DialogCallback = (idx) => {
      if (idx === 0) {
        this.taskService.stopAfterCurrentFile(taskId).subscribe();
        this.stopReqId = taskId;
      } else if (idx === 1) {
        this.taskService.stopNow(taskId).subscribe();
        this.stopReqId = taskId;
      }
    };

    if (txt.indexOf('Backup_') === 0) {
      this.dialog.dialog('Stop running backup',
        'You can stop the backup after any file uploads currently in progress have finished.',
        ['Stop after current file', 'Stop now', 'Cancel'],
        handleClick);
    } else {
      this.dialog.dialog('Stop running task',
        'You can stop the task immediately, or allow the process to continue its current file and then stop.',
        ['Stop after the current file', 'Stop now', 'Cancel'],
        handleClick);
    }
  }
}
