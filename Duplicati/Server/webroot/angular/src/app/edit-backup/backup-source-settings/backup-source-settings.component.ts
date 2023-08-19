import { EventEmitter, Input } from '@angular/core';
import { Output } from '@angular/core';
import { Component } from '@angular/core';
import { Backup } from '../../backup';

@Component({
  selector: 'app-backup-source-settings',
  templateUrl: './backup-source-settings.component.html',
  styleUrls: ['./backup-source-settings.component.less']
})
export class BackupSourceSettingsComponent {
  @Input({ required: true }) backup!: Backup;
  @Output() backupChange = new EventEmitter<Backup>();
  @Output() next = new EventEmitter<void>();
  @Output() prev = new EventEmitter<void>();

  editSourceAdvanced: boolean = false;
  showhiddenfolders: boolean = false;
  manualSourcePath: string = '';
  validatingSourcePath: boolean = false;
  fileAttributes: any[] = [];
  excludeAttributes: string[] = [];
  excludeFileSize: number | null = null;
  showFilter: boolean = false;
  editFilterAdvanced: boolean = false;
  showExclude: boolean = false;

  addManualSourcePath() { }
}
