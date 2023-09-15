import { ViewChild } from '@angular/core';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { Backup } from '../../backup';
import { CopyClipboardButtonsComponent } from '../../dialog-templates/copy-clipboard-buttons/copy-clipboard-buttons.component';
import { DialogService } from '../../services/dialog.service';
import { BackupEditUriComponent } from '../backup-edit-uri/backup-edit-uri.component';
import { BackupOptions } from '../backup-options';

@Component({
  selector: 'app-backup-destination-settings',
  templateUrl: './backup-destination-settings.component.html',
  styleUrls: ['./backup-destination-settings.component.less']
})
export class BackupDestinationSettingsComponent {
  @Output() next = new EventEmitter<void>();
  @Output() prev = new EventEmitter<void>();

  @ViewChild(BackupEditUriComponent)
  editUri!: BackupEditUriComponent;

  constructor(private dialog: DialogService) { }

  importUrl(): void {
    this.dialog.textareaDialog('Import URL', 'Enter a Backup destination URL:', 'Enter URL', '', ['Cancel', 'OK'], undefined, (btn, input) => {
      if (btn === 1 && input !== undefined) {
        this.editUri.setUri(input, true);
      }
    });
  }
  copyUrlToClipboard(): void {
    const uri = this.editUri.buildUri();
    if (uri != null) {
      this.dialog.textareaDialog('Copy URL', '', undefined, uri, ['OK'], CopyClipboardButtonsComponent);
    }
  }
  getUri(): string | null {
    return this.editUri.buildUri();
  }
  setUri(uri: string) {
    this.editUri.setUri(uri);
  }
}
