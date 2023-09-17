import { TmplAstBoundEvent } from '@angular/compiler';
import { Component, ViewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { defaultIfEmpty } from 'rxjs';
import { single } from 'rxjs';
import { map, Subscription } from 'rxjs';
import { AddOrUpdateBackupData, Backup } from '../backup';
import { CopyClipboardButtonsComponent } from '../dialog-templates/copy-clipboard-buttons/copy-clipboard-buttons.component';
import { BackupEditUriComponent } from '../edit-backup/backup-edit-uri/backup-edit-uri.component';
import { BackupListService } from '../services/backup-list.service';
import { BackupService } from '../services/backup.service';
import { ConvertService } from '../services/convert.service';
import { DialogService } from '../services/dialog.service';
import { ImportService } from '../services/import.service';
import { ParserService } from '../services/parser.service';
import { RestoreService } from '../services/restore.service';
import { ServerStatus } from '../services/server-status';
import { ServerStatusService } from '../services/server-status.service';

@Component({
  selector: 'app-restore-direct',
  templateUrl: './restore-direct.component.html',
  styleUrls: ['./restore-direct.component.less']
})
export class RestoreDirectComponent {
  connecting: boolean = false;
  connectionProgress: string = '';
  taskid?: string;
  serverstate?: ServerStatus;
  encryptionPassphrase?: string;
  showAdvanced: boolean = false;
  extendedOptions: string[] = [];
  backupId?: string;

  @ViewChild(BackupEditUriComponent)
  editUri!: BackupEditUriComponent;

  private subscription?: Subscription;

  constructor(public convert: ConvertService,
    public parser: ParserService,
    private dialog: DialogService,
    private router: Router,
    private route: ActivatedRoute,
    private backupList: BackupListService,
    private restoreService: RestoreService,
    private importService: ImportService,
    private backupService: BackupService,
    public serverStatus: ServerStatusService) { }

  ngOnInit() {
    // Replace current history state, so that navigating back skips past the last one
    this.router.navigate([{ step: 0 }], { relativeTo: this.route, replaceUrl: true });
    this.subscription = this.serverStatus.getStatus().subscribe(s => this.serverstate = s);
  }

  ngAfterViewInit() {
    if (this.route.snapshot.data['import'] === true) {
      // Transfer data from import. Need to wait for next change detection cycle
      setTimeout(() => this.importBackupData());
    }
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  importBackupData() {
    let backup = this.importService.getImportData();
    if (backup == null) {
      this.router.navigate(['restoredirect'], { replaceUrl: true });
    }
    if (backup?.Backup != null && backup.Backup.TargetURL != null) {
      this.editUri.setUri(backup.Backup.TargetURL);
      let tmpsettings = backup.Backup.Settings ? [...backup.Backup.Settings] : [];
      let res: Record<string, string> = {};
      for (let s of tmpsettings) {
        if (s.Name === 'passphrase') {
          this.encryptionPassphrase = s.Value;
        } else {
          let optname = s.Name;
          if (!optname.startsWith('--')) {
            optname = '--' + optname;
          }
          res[optname] = s.Value;
        }
      }
      this.showAdvanced = true;
      this.extendedOptions = this.parser.serializeAdvancedOptionsToArray(res);
    }
  }

  importUrl(): void {
    this.dialog.textareaDialog($localize`Import URL`, $localize`Enter a Backup destination URL:`, $localize`Enter URL`, '', [$localize`Cancel`, $localize`OK`], undefined, (btn, input) => {
      if (btn === 1 && input !== undefined) {
        this.editUri.setUri(input, true);
      }
    });
  }
  copyUrlToClipboard(): void {
    this.editUri.buildUri().subscribe(uri =>
      this.dialog.textareaDialog($localize`Copy URL`, '', undefined, uri, [$localize`OK`], CopyClipboardButtonsComponent));
  }

  nextPage() {
    let currentStep = parseInt(this.route.snapshot.paramMap.get('step') || '0');
    this.router.navigate([{ step: Math.min(1, currentStep + 1) }], { relativeTo: this.route });
  }
  prevPage() {
    let currentStep = parseInt(this.route.snapshot.paramMap.get('step') || '0');
    this.router.navigate([{ step: Math.max(0, currentStep - 1) }], { relativeTo: this.route });
  }

  doConnect() {
    this.editUri.buildUri().pipe(defaultIfEmpty(null)).subscribe(
      targetURL => {
        if (targetURL == null) {
          this.router.navigate([{ step: 0 }], { relativeTo: this.route });
          return;
        }

        this.connecting = true;
        this.connectionProgress = $localize`Registering temporary backup …`;

        let opts: Record<string, string> = {};
        let obj: { Backup: Partial<Backup> } = { Backup: { TargetURL: targetURL } };
        if ((this.encryptionPassphrase || '') == '') {
          opts['--no-encryption'] = 'true';
        } else {
          opts['passphrase'] = this.encryptionPassphrase!;
        }

        if (!this.parser.parseExtraOptions(this.extendedOptions, opts)) {
          return;
        }

        obj.Backup.Settings = [];
        for (let k in opts) {
          obj.Backup.Settings.push({
            Name: k, Value: opts[k],
            Argument: null,
            Filter: ''
          });
        }

        this.backupList.createTemporaryBackup(obj).subscribe(
          id => {
            this.connectionProgress = $localize`Listing backup dates …`;
            this.backupId = id;
            this.fetchBackupTimes();
          },
          err => {
            this.connecting = false;
            this.connectionProgress = '';
            this.dialog.connectionError($localize`Failed to connect: `, err);
          }
        );
      });
  }

  fetchBackupTimes() {
    if (this.backupId == null) {
      return;
    }
    this.backupService.getFilesets(this.backupId).subscribe(
      filesets => {
        // Pass filesets to restore component
        this.restoreService.setTemporaryFileset(this.backupId!, filesets);
        this.router.navigate(['/restore', this.backupId!]);
      },
      err => {
        let message = err.statusText;
        if (err.error != null && err.error.Message != null) {
          message = err.error.Message;
        }
        if (message === 'encrypted-storage') {
          message = $localize`The target folder contains encrypted files, please supply the passphrase`;
        }

        this.connecting = false;
        this.connectionProgress = '';
        this.dialog.dialog($localize`Error`, $localize`Failed to connect: ` + message);
      });
  }
}
