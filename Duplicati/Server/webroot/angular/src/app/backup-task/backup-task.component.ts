import { SimpleChanges } from '@angular/core';
import { Component, Input } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AddOrUpdateBackupData } from '../backup';
import { BackupService } from '../services/backup.service';
import { ConvertService } from '../services/convert.service';
import { ServerStatus } from '../services/server-status';
import { ServerStatusService } from '../services/server-status.service';

@Component({
  selector: 'app-backup-task',
  templateUrl: './backup-task.component.html',
  styleUrls: ['./backup-task.component.less']
})
export class BackupTaskComponent {
  @Input() backup!: AddOrUpdateBackupData;
  state?: ServerStatus;
  expanded: boolean = false;

  backupName?: string;
  backupId?: string;
  isScheduled: boolean = false;
  isActive: boolean = false;
  isRunning: boolean = false;
  isPaused: boolean = false;
  description?: string;
  lastBackupFinished?: Date;
  lastBackupFinishedTime?: string;
  lastBackupFinishedDuration?: string;
  nextScheduledRun?: Date;
  nextScheduledRunDate?: string;
  sourceSizeString?: string;
  targetSizeString?: string;
  backupListCount: number = 0;
  progressCurrentFilename?: string;
  progressPhase?: string;
  progressCurrentFilesize?: number;
  progressCurrentFileoffset?: number;
  progressBarPercentage: number = 0;

  backupIcon: string = 'backup;'

  private subscription?: Subscription;

  constructor(private router: Router,
    private backupService: BackupService,
    private serverStatus: ServerStatusService,
    private convert: ConvertService) { }

  ngOnInit() {
    this.subscription = this.serverStatus.getStatus().subscribe(s => {
      this.state = s;
      this.updateProgress();
    });
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ('backup' in changes) {
      this.updateBackup(this.backup);
    }
  }

  private updateBackupIcon() {
    if (!this.isActive && this.isScheduled) {
      this.backupIcon = 'backupschedule';
    } else if (this.isRunning) {
      this.backupIcon = 'backuprunning';
    } else if (this.isPaused) {
      this.backupIcon = 'backuppause';
    } else {
      this.backupIcon = 'backup';
    }
  }

  updateBackup(b: AddOrUpdateBackupData) {
    this.backupName = b.Backup.Name;
    this.backupId = b.Backup.ID;
    this.isScheduled = 'NextScheduledRun' in b.Backup.Metadata;
    this.isActive = b.Backup.ID === this.state?.activeTask?.Item2;
    this.isRunning = this.isActive && this.state?.programState === 'Running';
    this.isPaused = this.isActive && this.state?.programState === 'Paused';
    this.description = b.Backup.Description;
    this.lastBackupFinished = this.convert.parseTimestamp(b.Backup.Metadata['LastBackupFinished']);
    this.lastBackupFinishedTime = this.lastBackupFinished ? this.convert.formatDate(this.lastBackupFinished) : undefined;
    this.lastBackupFinishedDuration = this.convert.formatDuration(b.Backup.Metadata['LastBackupDuration'] || b.Backup.Metadata['LastDuration']);
    this.nextScheduledRun = this.convert.parseTimestamp(b.Backup.Metadata['NextScheduledRun']);
    this.nextScheduledRunDate = this.nextScheduledRun ? this.convert.formatDate(this.nextScheduledRun) : undefined;
    this.sourceSizeString = b.Backup.Metadata['SourceSizeString'];
    this.targetSizeString = b.Backup.Metadata['TargetSizeString'];
    this.backupListCount = parseInt(b.Backup.Metadata['BackupListCount'] || '0');
    this.updateBackupIcon();
  }


  updateProgress() {
    this.isActive = this.backupId === this.state?.activeTask?.Item2;
    this.isRunning = this.isActive && this.state?.programState === 'Running';
    this.isPaused = this.isActive && this.state?.programState === 'Paused';
    this.progressPhase = this.state?.lastPgEvent?.Phase;
    this.progressCurrentFilename = this.state?.lastPgEvent?.CurrentFilename;
    this.progressCurrentFilesize = this.state?.lastPgEvent?.CurrentFilesize;
    this.progressCurrentFileoffset = this.state?.lastPgEvent?.CurrentFileoffset;
    if (this.progressCurrentFilesize !== undefined && this.progressCurrentFileoffset !== undefined) {
      this.progressBarPercentage = (1 - (this.progressCurrentFilesize! - this.progressCurrentFileoffset) / this.progressCurrentFilesize) * 100;
    } else {
      this.progressBarPercentage = 0;
    }
    this.updateBackupIcon();
  }

  doRun(): void {
    this.backupService.doRun(this.backupId!);
  }
  doRestore(): void { this.router.navigate(['restore', this.backupId!]); }
  doEdit(): void { this.router.navigate(['edit', this.backupId!]); }
  doExport(): void { this.router.navigate(['export', this.backupId!]); }
  doDelete(): void { this.router.navigate(['delete', this.backupId!]); }
  doLocalDb(): void { this.router.navigate(['localdb', this.backupId!]); }
  doCompact(): void {
    this.backupService.doCompact(this.backupId!);
  }
  doCommandLine(): void { this.router.navigate(['commandline', this.backupId!]); }
  doShowLog(): void { this.router.navigate(['log', this.backupId!]); }
  doCreateBugReport(): void {
    this.backupService.doCreateBugReport(this.backupId!);
  }
  doVerifyRemote(): void {
    this.backupService.doVerifyRemote(this.backupId!);
  }
}
