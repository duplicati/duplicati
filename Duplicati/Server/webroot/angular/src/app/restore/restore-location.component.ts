import { EventEmitter, Output } from '@angular/core';
import { Component, Input } from '@angular/core';
import { AddOrUpdateBackupData, Backup } from '../backup';
import { DialogService } from '../services/dialog.service';

export class RestoreLocationData {
  restoreLocation: 'direct' | 'custom' = 'direct';
  restorePath: string = '';
  restoreMode: 'overwrite' | 'copy' = 'overwrite';
  restorePermissions: boolean = false;
  passphrase: string = '';
}

@Component({
  selector: 'app-restore-location',
  templateUrl: './restore-location.component.html',
  styleUrls: ['./restore-location.component.less']
})
export class RestoreLocationComponent {
  @Input({ required: true }) backup?: AddOrUpdateBackupData;
  @Output() restore = new EventEmitter<RestoreLocationData>();
  @Output() prev = new EventEmitter<void>();

  data = new RestoreLocationData();

  get restorePath(): string {
    return this.data.restorePath;
  }
  set restorePath(path: string) {
    this.data.restorePath = path;
    if (path.length == 0) {
      this.data.restoreLocation = 'direct';
    } else {
      this.data.restoreLocation = 'custom';
    }
  }

  hideFolderBrowser: boolean = true;
  showHiddenFolders: boolean = false;

  get showInputPassphrase(): boolean {
    if (!this.backup) {
      return false;
    }
    return !this.backup.IsUnencryptedOrPassphraseStored;
  }

  constructor(private dialog: DialogService) { }
}
