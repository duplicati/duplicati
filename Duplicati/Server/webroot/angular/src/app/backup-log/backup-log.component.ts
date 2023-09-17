import { Input, SimpleChanges } from '@angular/core';
import { Component } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subject, Subscription } from 'rxjs';
import { AddOrUpdateBackupData, Backup } from '../backup';
import { BackupListService } from '../services/backup-list.service';
import { BackupService } from '../services/backup.service';
import { DialogService } from '../services/dialog.service';
import { BackupLogEntry, RemoteLogEntry } from '../services/log-entry';
import { LogService } from '../services/log.service';

@Component({
  selector: 'app-backup-log',
  templateUrl: './backup-log.component.html',
  styleUrls: ['./backup-log.component.less']
})
export class BackupLogComponent {
  @Input({ required: true }) backupId!: string;

  backup?: Backup;
  remoteData?: RemoteLogEntry[];
  generalData?: BackupLogEntry[];

  readonly pageSize = 15;
  page: string = 'general';
  loadingData: boolean = false;
  generalDataComplete: boolean = false;
  remoteDataComplete: boolean = false;

  private generalPageSubscription?: Subscription;
  private remotePageSubscription?: Subscription;
  private nextGeneralPage$ = new Subject<void>();
  private nextRemotePage$ = new Subject<void>();

  constructor(private logService: LogService,
    private dialog: DialogService,
    private backupService: BackupService,
    private route: ActivatedRoute) { }

  ngOnChanges(changes: SimpleChanges) {
    if ('backupId' in changes && !changes['backupId'].isFirstChange()) {
      // Reload everything
      this.generalPageSubscription?.unsubscribe();
      this.remotePageSubscription?.unsubscribe();
      this.ngOnInit();
    }
  }

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      this.setPage(params.get('page') || 'general');
    });
    this.route.data.subscribe(data => {
      const backupData: AddOrUpdateBackupData = data['backup'];
      this.backup = backupData.Backup;
    });
  }

  ngOnDestroy() {
    this.remotePageSubscription?.unsubscribe();
  }

  setPage(p: string) {
    this.page = p;
    if (p == 'general' && this.generalPageSubscription == null) {
      this.generalPageSubscription = this.logService.getBackupLog(this.backupId, this.pageSize, this.nextGeneralPage$).subscribe({
        next: logs => {
          if (this.generalData == null) {
            this.generalData = logs;
          } else {
            this.generalData.push(...logs);
          }
        },
        error: this.dialog.connectionError($localize`Failed to connect: `),
        complete: () => this.generalDataComplete = true
      });
      this.loadMoreGeneralData();
    } else if (p == 'remote' && this.remotePageSubscription == null) {
      this.remotePageSubscription = this.logService.getRemoteLog(this.backupId, this.pageSize, this.nextRemotePage$).subscribe({
        next: logs => {
          if (this.remoteData == null) {
            this.remoteData = logs;
          } else {
            this.remoteData.push(...logs);
          }
        },
        error: this.dialog.connectionError($localize`Failed to connect: `),
        complete: () => this.remoteDataComplete = true
      });
      this.loadMoreRemoteData();
    }
  }

  loadMoreGeneralData() {
    this.nextGeneralPage$.next();
  }

  loadMoreRemoteData() {
    this.nextRemotePage$.next();
  }
}
