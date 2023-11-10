import { SimpleChanges } from '@angular/core';
import { Component, Input } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { Backup } from '../backup';
import { BackupListService } from '../services/backup-list.service';
import { BackupService } from '../services/backup.service';
import { DialogService } from '../services/dialog.service';
import { FileService } from '../services/file.service';

@Component({
  selector: 'app-local-database',
  templateUrl: './local-database.component.html',
  styleUrls: ['./local-database.component.less']
})
export class LocalDatabaseComponent {
  @Input({ required: true }) backupId!: string;

  backup?: Backup;
  noLocalDB: boolean = true;
  dbPath?: string;

  private subscription?: Subscription;
  private subscriptionValidate?: Subscription;

  constructor(private backupService: BackupService,
    private backupList: BackupListService,
    private dialog: DialogService,
    private router: Router,
    private route: ActivatedRoute,
    private fileService: FileService) { }

  ngOnInit() {
    this.route.data.subscribe(data => {
      this.backup = data['backup'].Backup;
      this.updateDbPath();
    });
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
    this.subscriptionValidate?.unsubscribe();
  }

  updateDbPath(prev?: string, force?: boolean) {
    this.dbPath = undefined;
    if (this.backup == null) {
      this.noLocalDB = true;
    } else {
      this.dbPath = this.backup.DBPath;

      if (this.dbPath !== prev || force) {
        this.subscriptionValidate = this.fileService.validateFile(this.dbPath).subscribe(valid => {
          this.noLocalDB = !valid;
        });
      }
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
      this.updateDbPath(prev, force);
    });
  }

  doSave(move?: boolean, continuation?: () => void) {
    let doUpdate = () => {
      this.backupService.updateDatabase(this.backupId, this.dbPath!, move).subscribe(
        () => {
          this.backup!.DBPath = this.dbPath!;
          this.reloadBackupItem(true);
          if (continuation != null) {
            continuation();
          }
        },
        this.dialog.connectionError(move ? $localize`Move failed: ` : $localize`Update failed: `)
      );
    };

    let doCheckTarget = () => {
      this.fileService.validateFile(this.dbPath!).subscribe(valid => {
        if (valid) {
          this.dialog.dialog($localize`Existing file found`,
            $localize`An existing file was found at the new location\nAre you sure you want the database to point to an existing file?`,
            [$localize`Cancel`, $localize`No`, $localize`Yes`],
            (ix) => {
              if (ix == 2) {
                doUpdate();
              }
            }
          )
        } else {
          doUpdate();
        }
      });
    };

    if (move) {
      doUpdate();
    } else {
      if (this.noLocalDB) {
        doCheckTarget();
      } else {
        this.dialog.dialog($localize`Updating with existing database`,
          $localize`You are changing the database path away from an existing database.\nAre you sure this is what you want?`,
          [$localize`Cancel`, $localize`No`, $localize`Yes`],
          (ix) => {
            if (ix == 2) {
              doCheckTarget();
            }
          });
      }
    }
  }
  doSaveAndRepair() {
    this.doSave(false, () => this.doRepair());
  }
  doMove() {
    this.fileService.validateFile(this.dbPath!).subscribe(valid => {
      if (valid) {
        this.dialog.dialog($localize`Cannot move to existing file`,
          $localize`An existing file was found at the new location`);
      } else {
        this.doSave(true);
      }
    });
  }
  doRepair() {
    this.backupService.doRepair(this.backupId);
    this.router.navigate(['/']);
  }
  doDelete(continuation?: () => void) {
    this.dialog.dialog($localize`Confirm delete`,
      $localize`Do you really want to delete the local database for: ${this.backup?.Name}`,
      [$localize`No`, $localize`Yes`],
      (ix) => {
        if (ix == 1) {
          this.backupService.deleteDatabase(this.backupId).subscribe(() => {
            this.reloadBackupItem(true);
            if (continuation != null) {
              continuation();
            }
          }, err => {
            this.reloadBackupItem();
            this.dialog.connectionError($localize`Failed to delete: `, err);
          });
        }
      }
    );
  }
  doDeleteAndRepair() {
    this.doDelete(() => this.doRepair());
  }
}
