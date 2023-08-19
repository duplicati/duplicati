import { EventEmitter, Input } from '@angular/core';
import { Output } from '@angular/core';
import { Component } from '@angular/core';
import { Backup } from '../../backup';
import { ConvertService } from '../../services/convert.service';
import { FileFilterService } from '../../services/file-filter.service';
import { ParserService } from '../../services/parser.service';

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
  fileAttributes: ({ name: string, value: string })[] = [];
  excludeAttributes: string[] = [];
  excludeFileSize: number | null = null;
  showFilter: boolean = false;
  editFilterAdvanced: boolean = false;
  showExclude: boolean = false;


  private _excludeLargeFiles: boolean = false;
  private _fileSize: number | null = null;
  private _fileSizeMultiplier: string = '';
  set excludeLargeFiles(v: boolean) {
    this._excludeLargeFiles = v;
    if (v && this.fileSizeInput == null) {
      this._fileSize = 100;
      this._fileSizeMultiplier = 'MB';
    }
    this.excludeFileSize = v ? this.parser.parseSizeString(this._fileSize + this._fileSizeMultiplier) : null;
  }
  get excludeLargeFiles(): boolean {
    return this._excludeLargeFiles;
  }
  set fileSizeInput(s: number | null) {
    this._fileSize = s;
    if (s == null) {
      this.excludeLargeFiles = false;
    }
    this.excludeFileSize = this.excludeLargeFiles ? this.parser.parseSizeString(this._fileSize + this._fileSizeMultiplier) : null;
  }
  get fileSizeInput(): number | null {
    return this._fileSize;
  }
  set fileSizeMultiplier(m: string) {
    this._fileSizeMultiplier = m;
    this.excludeFileSize = this.excludeLargeFiles ? this.parser.parseSizeString(this._fileSize + this._fileSizeMultiplier) : null;
  }
  get fileSizeMultiplier(): string {
    return this._fileSizeMultiplier;
  }
  fileSizeMultipliers: ({ name: string, value: string })[] = [];

  constructor(private filterService: FileFilterService, private parser: ParserService) { }

  ngOnInit() {
    this.fileAttributes = this.filterService.getFileAttributes();
    this.fileSizeMultipliers = this.parser.fileSizeMultipliers;
  }

  addManualSourcePath() { }

  attributeExcluded(value: string): boolean {
    return this.excludeAttributes.includes(value);
  }

  excludeAttribute(value: string, exclude: boolean): void {
    const idx = this.excludeAttributes.indexOf(value);
    if (idx >= 0 && !exclude) {
      let copy = [...this.excludeAttributes];
      copy.splice(idx, 1);
      this.excludeAttributes = copy;
    } else if (idx < 0 && exclude) {
      this.excludeAttributes = [...this.excludeAttributes, value];
    }
  }
}
