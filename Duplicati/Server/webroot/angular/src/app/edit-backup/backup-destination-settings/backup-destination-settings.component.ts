import { Component, EventEmitter, Input, Output } from '@angular/core';
import { Backup } from '../../backup';
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

  importUrl(): void { }
  copyUrlToClipboard(): void { }
}
