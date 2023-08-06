import { Component } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { AddOrUpdateBackupData } from '../backup';
import { BackupListService } from '../services/backup-list.service';
import { ConvertService } from '../services/convert.service';
import { ProgressService } from '../services/progress.service';
import { ServerStatus } from '../services/server-status';
import { ServerStatusService } from '../services/server-status.service';

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
    private progressService: ProgressService
  ) { }
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

  }
}
