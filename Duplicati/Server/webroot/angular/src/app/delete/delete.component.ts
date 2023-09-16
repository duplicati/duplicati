import { Component, Input, SimpleChanges } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { Backup } from '../backup';
import { BackupListService } from '../services/backup-list.service';
import { BackupService } from '../services/backup.service';
import { CaptchaService } from '../services/captcha.service';
import { DialogService } from '../services/dialog.service';
import { FileService } from '../services/file.service';

@Component({
  selector: 'app-delete',
  templateUrl: './delete.component.html',
  styleUrls: ['./delete.component.less']
})
export class DeleteComponent {
  @Input({ required: true }) backupId!: string;

  backup?: Backup;
  noLocalDB: boolean = true;
  dbPath?: string;

  deleteLocalDatabase: boolean = true;
  deleteRemoteFiles: boolean = false;
  dbUsedElsewhere: boolean = false;

  private hasRefreshedRemoteSize: boolean = false;
  private listFilesTaskid?: string;

  get filecount(): string | undefined{
    return this.backup?.Metadata['TargetFilesCount'];
  }
  get filesize(): string | undefined {
    return this.backup?.Metadata['TargetSizeString'];
  }

  private subscription?: Subscription;
  private subscriptionValidate?: Subscription;
  private subscriptionUsed?: Subscription;

  constructor(private backupList: BackupListService,
    private backupService: BackupService,
    private dialog: DialogService,
    private captcha: CaptchaService,
    private router: Router,
    private route: ActivatedRoute,
    private fileService: FileService) { }

  ngOnInit() {
    this.route.data.subscribe(data => {
      this.backup = data['backup'].Backup;
      this.updateBackup();
    });
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
    this.subscriptionValidate?.unsubscribe();
    this.subscriptionUsed?.unsubscribe();
  }
  updateBackup(prevDbPath?: string, force?: boolean) {
    this.dbPath = undefined;
    if (this.backup == null) {
      this.noLocalDB = true;
      this.dbUsedElsewhere = false;
    } else {
      this.dbPath = this.backup.DBPath;

      if (this.dbPath !== prevDbPath || force) {
        this.subscriptionValidate = this.fileService.validateFile(this.dbPath).subscribe(valid => {
          this.noLocalDB = !valid;
        });
      }

      if (this.dbPath !== prevDbPath || force) {
        this.backupService.isDbUsedElsewhere(this.backupId, this.dbPath).subscribe(used => {
          this.dbUsedElsewhere = used;
          // Default to not delete the db if others use it
          if (used) {
            this.deleteLocalDatabase = false;
          }
        }, err => this.dbUsedElsewhere = true);
      }
    }

    if (this.backup != null && !this.hasRefreshedRemoteSize
      && (this.backup.Metadata['TargetFilesCount'] == null || parseInt(this.backup.Metadata['TargetFilesCount']) <= 0)) {
      this.hasRefreshedRemoteSize = true;
      this.backupService.startSizeReport(this.backupId).subscribe(taskid => this.listFilesTaskid = taskid,
        this.dialog.connectionError('Failed to refresh size: '));
    }
  }

  reloadBackupItem(force?: boolean) {
    const prev = this.dbPath;
    this.subscription?.unsubscribe();
    this.subscriptionValidate?.unsubscribe();
    this.subscription = this.backupList.getBackupsLookup().subscribe(backups => {
      if (this.backupId != null) {
        this.backup = backups[this.backupId]?.Backup;
      } else {
        this.backup = undefined;
      }
      this.updateBackup(prev, force);
    });
  }


  doExport() {
    this.router.navigate(['/export', this.backupId]);
  }
  doDelete() {
    if (this.deleteRemoteFiles) {
      this.captcha.authorize('Confirm delete',
        `To confirm you want to delete all remote files for "${this.backup?.Name}", please enter the word you see below`,
        `DELETE /backup/${this.backupId}`, (token, answer) => {
          this.backupService.deleteBackup(this.backupId, this.deleteLocalDatabase, this.deleteRemoteFiles, token, answer).subscribe(
            () => this.router.navigate(['/']),
            this.dialog.connectionError('Failed to delete backup: ')
          );
        });
    } else {
      this.dialog.dialog('Confirm delete',
        `Do you really want to delete the backup: "${this.backup?.Name}" ?`,
        ['No', 'Yes'],
        (ix) => {
          if (ix == 1) {
            this.backupService.deleteBackup(this.backupId, this.deleteLocalDatabase, this.deleteRemoteFiles).subscribe(
              () => this.router.navigate(['/']),
              this.dialog.connectionError('Failed to delete backup: ')
            );
          }
        });
    }
  }
  goBack() {
    this.router.navigate(['/']);
  }
}
