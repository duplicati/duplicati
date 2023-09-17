import { Component, Input } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { switchMap, take } from 'rxjs';
import { AddOrUpdateBackupData, Fileset, ListFile } from '../backup';
import { BackupListService } from '../services/backup-list.service';
import { BackupService } from '../services/backup.service';
import { DialogService } from '../services/dialog.service';
import { FileService } from '../services/file.service';
import { GroupedOptions, GroupedOptionService } from '../services/grouped-option.service';
import { LabeledFileset, RestoreService } from '../services/restore.service';
import { ServerStatus } from '../services/server-status';
import { ServerStatusService } from '../services/server-status.service';
import { TaskService } from '../services/task.service';
import { SystemInfo } from '../system-info/system-info';
import { RestoreLocationData } from './restore-location.component';
import { RestoreData } from './restore.resolver';

@Component({
  selector: 'app-restore',
  templateUrl: './restore.component.html',
  styleUrls: ['./restore.component.less']
})
export class RestoreComponent {
  @Input() backupId!: string;

  isBackupTemporary: boolean = false;
  get restoreStep(): number {
    let step = parseInt(this.route.snapshot.paramMap.get('step') || '0');
    if (this.isBackupTemporary && step > 1) {
      return step - 2;
    }
    return step;
  }
  set restoreStep(step: number) {
    this.router.navigate([{ step: this.isBackupTemporary ? step + 2 : step }], { relativeTo: this.route });
  }

  backup?: AddOrUpdateBackupData;
  connectionProgress?: string;
  connecting: boolean = false;
  restoreStamp?: string;

  filesets: Fileset[] = [];
  selected: string[] = [];
  systemInfo?: SystemInfo;
  taskid?: number;

  // Donation amounts
  smallamount = '10€';
  largeamount = '100€';

  constructor(private router: Router,
    private route: ActivatedRoute,
    private backupService: BackupService,
    private backupList: BackupListService,
    private restoreService: RestoreService,
    private fileService: FileService,
    private serverStatus: ServerStatusService,
    private taskService: TaskService,
    private dialog: DialogService) { }

  ngOnInit() {
    this.route.data.subscribe(data => {
      const restoreData: RestoreData = data['restore'];
      this.isBackupTemporary = restoreData.isTemp;
      this.restoreStep = 0;
      if (restoreData.tempFilesets != null) {
        this.filesets = restoreData.tempFilesets;
      } else {
        this.backup = restoreData.backup;
        this.fetchBackupTimes();
      }
    });
  }

  fetchBackupTimes() {
    this.connecting = true;
    this.connectionProgress = $localize`Getting file versions …`;

    const fromRemoteOnly = this.isBackupTemporary;
    this.backupService.getFilesets(this.backupId, undefined, fromRemoteOnly).subscribe(filesets => {
      this.connecting = false;
      this.connectionProgress = '';
      this.filesets = filesets;
    }, err => {
      this.connecting = false;
      this.connectionProgress = '';
      this.dialog.connectionError($localize`Failed to connect: `, err);
    });
  }

  onClickNext() {
    let results = this.selected;
    if (results.length == 0) {
      this.dialog.dialog($localize`No items selected`, $localize`No items to restore, please select one or more items`);
    } else {
      this.restoreStep = 1;
    }
  }

  onClickBack() {
    // TODO: Keep data
    this.router.navigate(["/restoredirect"]);
  }

  setProgressDelay(progress: string | null) {
    this.connectionProgress = progress || '';
    // Have to delay, because this runs after change detection
    setTimeout(() => this.connecting = progress != null);
  }
  onStartRestore(locationData: RestoreLocationData) {
    if (locationData.restoreLocation == 'custom' && locationData.restorePath.length == 0) {
      this.dialog.alert($localize`You have chosen to restore to a new location, but not entered one`);
      return;
    }

    let dirsep = this.fileService.dirsep;
    if (this.selected.length > 0) {
      dirsep = this.fileService.guessDirsep(this.selected[0]);
    }
    if (locationData.restoreLocation != 'custom' && dirsep != this.fileService.dirsep) {
      this.dialog.confirm($localize`This backup was created on another operating system. Restoring files without specifying a destination folder can cause files to be restored in unexpected places. Are you sure you want to continue without choosing a destination folder?`,
        (ix) => {
          if (ix == 1) {
            this.onStartRestoreProcess(locationData, dirsep);
          }
        });
    } else {
      this.onStartRestoreProcess(locationData, dirsep);
    }
  }

  onStartRestoreProcess(locationData: RestoreLocationData, dirsep: string) {
    let stamp = this.restoreStamp;
    if (stamp == null) {
      this.restoreStep = 0;
      return;
    }
    this.restoreStep = 2;

    let handleError = (err: any) => {
      this.restoreStep = 1;
      this.connecting = false;
      this.connectionProgress = '';
      this.dialog.connectionError($localize`Failed to connect: `, err);
    };

    let p: any = {
      time: stamp,
      'restore-path': locationData.restoreLocation == 'custom' ? locationData.restorePath : null,
      overwrite: locationData.restoreMode == 'overwrite',
      permissions: locationData.restorePermissions,
      passphrase: locationData.passphrase
    };

    let paths: string[] = [];
    for (let item of this.selected) {
      if (item.endsWith(dirsep)) {
        // To support the possibility of encountering paths
        // with literal wildcard characters, but also being
        // able to add the globbing "*" suffix, use a regular
        // expression filter

        // Escape regular expression metacharacters
        var itemRegex = item.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        // Add "globbing" suffix
        paths.push('[' + itemRegex + '.*]');
      } else {
        // To support the possibility of encountering paths
        // with literal wildcard characters, create a literal
        // filter
        paths.push('@' + item);
      }
    }

    if (paths.length > 0) {
      p.paths = JSON.stringify(paths);
    }

    if (this.isBackupTemporary) {
      this.connecting = true;
      this.connectionProgress = $localize`Creating temporary backup …`;

      let tempId: string | undefined;
      this.backupService.copyToTemp(this.backupId).pipe(
        switchMap(tmpId => {
          tempId = tmpId;
          this.connectionProgress = $localize`Building partial temporary database …`;
          return this.backupService.repair(tmpId, p);
        })
      ).subscribe(
        taskid => {
          this.taskid = taskid;
          this.serverStatus.callWhenTaskCompletes(taskid, () => {
            this.taskService.getStatus(taskid).subscribe(status => {
              this.connectionProgress = $localize`Starting the restore process …`;
              if (status.Status == 'Completed') {
                this.backupService.restore(tempId!, p).subscribe(taskid => {
                  this.connectionProgress = $localize`Restoring files …`;
                  this.taskid = taskid;
                  this.serverStatus.callWhenTaskCompletes(taskid, () => this.onRestoreComplete(taskid));
                }, handleError);
              } else if (status.Status == 'Failed') {
                this.dialog.dialog($localize`Error`, $localize`Failed to build temporary database: ` + status.ErrorMessage);
                this.connecting = false;
                this.connectionProgress = '';
                this.restoreStep = 1;
              }
            }, handleError);
          });
        },
        handleError
      );
    } else {
      this.connecting = true;
      this.connectionProgress = $localize`Starting the restore process …`;
      this.backupService.restore(this.backupId, p).subscribe(taskid => {
        this.connectionProgress = $localize`Restoring files …`;
        this.taskid = taskid;
        this.serverStatus.callWhenTaskCompletes(taskid, () => this.onRestoreComplete(taskid));
      }, handleError);
    }
  }

  onRestoreComplete(taskid: number) {
    this.taskService.getStatus(taskid).subscribe(status => {
      this.connecting = false;
      this.connectionProgress = '';
      if (status.Status == 'Completed') {
        this.restoreStep = 3;
      } else if (status.Status == 'Failed') {
        this.dialog.dialog($localize`Error`, $localize`Failed to restore files: ` + status.ErrorMessage);
      }
    }, err => {
      this.restoreStep = 1;
      this.connecting = false;
      this.connectionProgress = '';
      this.dialog.connectionError($localize`Failed to connect: `, err);
    });
  }
}
