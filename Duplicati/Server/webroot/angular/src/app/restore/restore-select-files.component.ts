import { Component, EventEmitter, Input, Output, SimpleChanges } from '@angular/core';
import { AddOrUpdateBackupData, Fileset, ListFile } from '../backup';
import { SearchTreeNode } from '../restore-file-picker/restore-file-database';
import { BackupService } from '../services/backup.service';
import { DialogService } from '../services/dialog.service';
import { FileService } from '../services/file.service';
import { GroupedOptions, GroupedOptionService } from '../services/grouped-option.service';
import { LabeledFileset, RestoreService } from '../services/restore.service';
import { ServerStatusService } from '../services/server-status.service';
import { TaskService } from '../services/task.service';

@Component({
  selector: 'app-restore-select-files',
  templateUrl: './restore-select-files.component.html',
  styleUrls: ['./restore-select-files.component.less']
})
export class RestoreSelectFilesComponent {
  @Input({ required: true }) isBackupTemporary!: boolean;
  @Input({ required: true }) backupId!: string;
  @Input({ required: true }) restoreStep!: number;
  @Input({ required: true }) connecting!: boolean;
  @Input() selected: string[] = [];
  @Output() selectedChange = new EventEmitter<string[]>();
  @Input({ alias: 'filesets', required: true }) filesetInput!: Fileset[];
  @Output() restoreStamp = new EventEmitter<string>();

  @Output() connectionProgress = new EventEmitter<string | null>();
  @Output() next = new EventEmitter<void>();
  @Output() prev = new EventEmitter<void>();

  backup?: AddOrUpdateBackupData;
  private _restoreVersion: number = 0;
  get restoreVersion(): number {
    return this._restoreVersion;
  }
  set restoreVersion(value: number) {
    this._restoreVersion = value;
    let stamp = this.filesetStamps.get(value + '');
    if (stamp != null) {
      this.restoreStamp.emit(stamp);
      this.fetchPathInformation();
    }
  }
  searchFilter: string = '';
  searching: boolean = false;
  paths: ListFile[] = [];

  filesets: LabeledFileset[] = [];
  groupedFilesets: GroupedOptions<LabeledFileset> = [];
  filesetStamps = new Map<string, string>();
  filesetsBuilt = new Map<string, ListFile[]>();
  filesetsRepaired = new Map<string, boolean>();
  inProgress = new Map<string, number | boolean>();
  temporaryDB?: string;
  taskid?: number;
  searchNodes: SearchTreeNode[] | null = null;

  get inSearchMode(): boolean {
    return this.searchNodes != null;
  }

  constructor(private restoreService: RestoreService,
    private backupService: BackupService,
    private dialog: DialogService,
    private taskService: TaskService,
    private fileService: FileService,
    private serverStatus: ServerStatusService,
    private groupService: GroupedOptionService) { }

  ngOnChanges(changes: SimpleChanges) {
    if ('filesetInput' in changes) {
      this.setFilesets(this.filesetInput);
    }
  }

  setFilesets(filesets: Fileset[]) {
    this.filesets = this.restoreService.parseBackupTimesData(filesets);
    this.groupedFilesets = this.groupService.groupOptions(this.filesets, v => v.GroupLabel, this.groupService.compareFields(v => v.Version));
    for (const fileset of filesets) {
      this.filesetStamps.set(fileset.Version + '', fileset.Time);
    }
    this.restoreVersion = 0;
    if (filesets.length > 0) {
      this.fetchPathInformation();
    }
  }

  fetchPathInformation() {
    const version = this.restoreVersion + '';
    if (this.connecting) {
      return;
    }

    if (this.inProgress.has(version) || this.restoreStep != 0) {
      return;
    }

    if (!this.isBackupTemporary && this.temporaryDB == null) {
      // TODO: register a temporary db here
    }

    let handleError = (err: any) => {
      this.inProgress.delete(version);
      this.connecting = false;
      this.connectionProgress.emit(null);
      this.dialog.connectionError('Failed to connect: ', err);
    };

    if (!this.filesetsBuilt.has(version)) {
      if (this.isBackupTemporary && !this.filesetsRepaired.has(version)) {
        this.connecting = true;
        this.connectionProgress.emit('Fetching path information …');
        this.inProgress.set(version, true);
        this.backupService.repairUpdateTemporary(this.backupId, this.filesetStamps.get(version)!).subscribe(
          taskid => {
            this.taskid = taskid;
            this.inProgress.set(version, taskid);
            this.serverStatus.callWhenTaskCompletes(taskid, () => {
              this.taskService.getStatus(taskid).subscribe(status => {
                this.inProgress.delete(version);
                this.connecting = false;
                this.connectionProgress.emit(null);
                if (status.Status === 'Completed') {
                  this.filesetsRepaired.set(version, true);
                  this.fetchPathInformation();
                } else if (status.Status === 'Failed') {
                  this.dialog.dialog('Error', 'Failed to get path information: ' + status.ErrorMessage)
                }
              },
                handleError);
            });
          },
          handleError
        );
      } else {
        const stamp = this.filesetStamps.get(version);
        // In case the times are not loaded yet
        if (stamp == null) {
          return;
        }

        this.connecting = true;
        this.connectionProgress.emit('Fetching path information …');
        this.inProgress.set(version, true);

        this.backupService.searchFiles(this.backupId, null, stamp, { prefixOnly: true, folderContents: false }).subscribe(
          res => {
            this.inProgress.delete(version);
            this.connecting = false;
            this.connectionProgress.emit(null);

            this.filesetsBuilt.set(version, res.Files);
            this.paths = res.Files;
          },
          handleError
        );
      }
    } else {
      this.paths = this.filesetsBuilt.get(version)!;
    }
  }

  doSearch() {
    if (this.searching || this.restoreStep != 0) {
      return;
    }
    if (this.searchFilter.trim().length == 0) {
      this.clearSearch();
      return;
    }

    this.searching = true;
    const version = this.restoreVersion + '';
    const stamp = this.filesetStamps.get(version)!;

    this.backupService.searchFiles(this.backupId, this.searchFilter, stamp).subscribe(
      resp => {
        this.searching = false;

        let searchNodes: SearchTreeNode[] = [];

        for (let i in this.filesetsBuilt.get(version) ?? []) {
          searchNodes[i] = { File: this.filesetsBuilt.get(version)![i] };
        }

        let files = resp.Files;
        for (let f of files) {
          const p = f.Path;
          const cp = this.fileService.comparablePath(p);
          const isdir = p.endsWith(this.fileService.dirsep);

          for (let i = 0; i < searchNodes.length; i++) {
            let sn = searchNodes[i];
            if (cp.startsWith(this.fileService.comparablePath(sn.File.Path))) {
              let curpath = sn.File.Path;
              let parts = p.substr(sn.File.Path.length).split(this.fileService.dirsep);
              // Remove empty part if path had dirsep at end
              if (parts.length > 0 && parts[parts.length - 1].length == 0) {
                parts.pop();
              }
              let col = sn;

              for (let k = 0; k < parts.length; k++) {
                curpath += parts[k];
                if (isdir || k != parts.length - 1) {
                  curpath += this.fileService.dirsep;
                }
                if (!col.Children) {
                  col.Children = [];
                }

                let found = col.Children.find(c => this.fileService.pathsEqual(c.File.Path, curpath));
                if (found != null) {
                  col = found;
                } else {
                  let n = { File: { Path: curpath, Sizes: [0] } };
                  col.Children!.push(n);
                  col = n;
                }
              }

              break;
            }
          }
        }
        this.searchNodes = searchNodes;
      },
      err => {
        this.searching = false;
        this.connecting = false;
        this.connectionProgress.emit(null);
        this.dialog.connectionError('Failed to connect: ', err);
      }
    );
  }

  searchChanged(value: string) {
    // This is a workaround to clear the search when 'X' pressed, without pressing enter
    if (value.length == 0) {
      this.clearSearch();
    }
  }

  clearSearch() {
    this.searchFilter = '';
    this.searchNodes = null;
  }
}
