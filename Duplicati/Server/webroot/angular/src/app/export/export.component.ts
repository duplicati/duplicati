import { Input } from '@angular/core';
import { Component } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { BackupService } from '../services/backup.service';
import { DialogService } from '../services/dialog.service';
import { UrlService } from '../services/url.service';

@Component({
  selector: 'app-export',
  templateUrl: './export.component.html',
  styleUrls: ['./export.component.less']
})
export class ExportComponent {
  @Input({ required: true }) backupId!: string;

  exportType: string = 'file';
  exportPasswords: boolean = true;
  connecting: boolean = false;
  completed: boolean = false;
  fileEncrypted: boolean = false;
  useEncryption: boolean = false;
  passphrase: string = '';
  confirmPassphrase: string = '';

  commandLine?: string;
  downloadURL?: SafeResourceUrl;

  constructor(private dialog: DialogService,
    private url: UrlService,
    private sanitizer: DomSanitizer,
    private backupService: BackupService) { }


  doExport() {
    let warnUnencryptedPasswords = (continuation: () => void) => {
      if (this.exportType === 'file' && this.exportPasswords && !this.fileEncrypted) {
        this.dialog.dialog($localize`Not using encryption`,
          $localize`The configuration should be kept safe. Are you sure you want to save an unencrypted file containing your passwords?`,
          [$localize`Cancel`, $localize`Yes, I understand the risk`],
          (ix) => {
            if (ix == 1) {
              continuation();
            }
          });
      } else {
        continuation();
      }
    };

    let getExport = () => {
      if (this.exportType === 'commandline') {
        this.connecting = true;
        this.backupService.exportCommandLine(this.backupId, this.exportPasswords).subscribe(
          cmd => {
            this.connecting = false;
            this.completed = true;
            this.commandLine = cmd;
          },
          err => {
            this.connecting = false;
            this.dialog.connectionError($localize`Failed to connect: `, err);
          }
        );
      } else {
        this.downloadURL = this.sanitizer.bypassSecurityTrustResourceUrl(this.url.getExportUrl(this.backupId, this.useEncryption ? this.passphrase : undefined, this.exportPasswords));
        this.completed = true;
      }
    };

    // Make checks that do not require user input
    this.fileEncrypted = false;
    if (this.useEncryption && this.exportType === 'file') {
      if (this.passphrase == null || this.passphrase.trim().length === 0) {
        this.dialog.dialog($localize`No passphrase entered`, $localize`To export without a passphrase, uncheck the \"Encrypt file\" box`);
      } else if (this.passphrase !== this.confirmPassphrase) {
        this.dialog.dialog($localize`Error`, $localize`The passwords do not match`);
      } else {
        this.fileEncrypted = true;
      }
    }

    warnUnencryptedPasswords(getExport);
  }
}
