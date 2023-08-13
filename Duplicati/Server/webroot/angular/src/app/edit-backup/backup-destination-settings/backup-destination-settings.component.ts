import { ViewChild } from '@angular/core';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { Backup } from '../../backup';
import { DialogService } from '../../services/dialog.service';
import { BackupEditUriComponent } from '../backup-edit-uri/backup-edit-uri.component';
import { BackupOptions } from '../backup-options';

@Component({
  selector: 'app-backup-destination-settings',
  templateUrl: './backup-destination-settings.component.html',
  styleUrls: ['./backup-destination-settings.component.less']
})
export class BackupDestinationSettingsComponent {
  @Input({ required: true }) backup!: Backup;
  @Input({ required: true }) options!: BackupOptions;
  @Output() backupChange = new EventEmitter<Backup>();
  @Output() optionsChange = new EventEmitter<BackupOptions>();
  @Output() next = new EventEmitter<void>();
  @Output() prev = new EventEmitter<void>();

  @ViewChild(BackupEditUriComponent)
  editUri!: BackupEditUriComponent;

  constructor(private dialog: DialogService) { }


  importUrl(): void {
    this.dialog.textareaDialog('Import URL', 'Enter a Backup destination URL:', 'Enter URL', '', ['Cancel', 'OK'], undefined, (btn, input) => {
      if (btn === 1 && input !== undefined) {
        this.backup.TargetURL = input;
        this.editUri.setUri(input, true);
      }
    });
  }
  copyUrlToClipboard(): void {
    this.editUri.buildUri(uri => {
      this.dialog.textareaDialog('Copy URL', '', undefined, uri, ['OK'], 'copy_clipboard_buttons.html',);
    });
  }
}
