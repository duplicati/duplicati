import { EventEmitter, Input, SimpleChanges } from '@angular/core';
import { Output } from '@angular/core';
import { Component } from '@angular/core';
import { Backup, BackupFilter } from '../../backup';
import { ConvertService } from '../../services/convert.service';
import { DialogService } from '../../services/dialog.service';
import { FileFilterService } from '../../services/file-filter.service';
import { FileService } from '../../services/file.service';
import { ParserService } from '../../services/parser.service';
import { BackupOptions } from '../backup-options';

@Component({
  selector: 'app-backup-source-settings',
  templateUrl: './backup-source-settings.component.html',
  styleUrls: ['./backup-source-settings.component.less']
})
export class BackupSourceSettingsComponent {
  @Input({ required: true }) backup!: Backup;
  @Input({ required: true }) options!: BackupOptions;
  @Output() backupChange = new EventEmitter<Backup>();
  @Output() optionsChange = new EventEmitter<BackupOptions>();
  @Output() next = new EventEmitter<void>();
  @Output() prev = new EventEmitter<void>();

  get sources(): string[] {
    return this.backup.Sources;
  }
  set sources(v: string[]) {
    this.backup = { ...this.backup, Sources: v };
    this.backupChange.emit(this.backup);
  }
  private _filters: string[] = [];
  get filters(): string[] {
    return this._filters;
  }
  set filters(v: string[]) {
    this.backup = {
      ...this.backup,
      Filters: v.map((v, i) => {
        return {
          Order: i,
          Include: v.startsWith('+'),
          Expression: v.substr(1)
        }
      })
    };
    this.backupChange.emit(this.backup);
  }

  editSourceAdvanced: boolean = false;
  showhiddenfolders: boolean = false;
  manualSourcePath: string = '';
  validatingSourcePath: boolean = false;
  fileAttributes: ({ name: string, value: string })[] = [];
  get excludeAttributes(): string[] {
    return this.options.excludeFileAttributes;
  }
  set excludeAttributes(value: string[]) {
    this.options = { ...this.options, excludeFileAttributes: value };
    this.optionsChange.emit(this.options);
  }
  get excludeFileSize(): number | null {
    return this.options.excludeFileSize;
  }
  set excludeFileSize(value: number | null) {
    this.options = { ...this.options, excludeFileSize: value };
    this.optionsChange.emit(this.options);
  }
  editFilterAdvanced: boolean = false;

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

  constructor(
    private dialog: DialogService,
    private fileService: FileService,
    private filterService: FileFilterService,
    private parser: ParserService) { }

  ngOnInit() {
    this.fileAttributes = this.filterService.getFileAttributes();
    this.fileSizeMultipliers = this.parser.fileSizeMultipliers;
  }

  ngOnChanges(changes: SimpleChanges) {
    if ('backup' in changes) {
      this._filters = this.backup.Filters.map(f => (f.Include ? '+' : '-') + f.Expression);
    }
  }

  private validateSourcePath(): void {
    this.validatingSourcePath = true;

    this.fileService.validateFile(this.manualSourcePath).subscribe(valid => {
      this.validatingSourcePath = false;
      if (valid) {
        this.sources = [...this.sources, this.manualSourcePath];
        this.manualSourcePath = '';
      } else {
        this.dialog.dialog($localize`Path not found`, $localize`The path does not appear to exist, do you want to add it anyway?`, [$localize`No`, $localize`Yes`], (ix) => {
          if (ix == 1) {
            this.sources = [...this.sources, this.manualSourcePath];
            this.manualSourcePath = '';
          }
        });
      }
    });
  }

  addManualSourcePath(): void {
    if (this.validatingSourcePath) {
      return;
    }
    if (this.manualSourcePath == null || this.manualSourcePath === '') {
      return;
    }
    if (this.sources.findIndex(s => this.fileService.pathsEqual(s, this.manualSourcePath)) >= 0) {
      this.manualSourcePath = '';
      return;
    }

    if (this.fileService.isValidSourcePath(this.manualSourcePath)) {
      this.dialog.dialog($localize`Relative paths not allowed`, $localize`The path must be an absolute path, i.e. it must start with a forward slash '/'`);
    }

    if (!this.manualSourcePath.endsWith(this.fileService.dirsep)) {
      this.dialog.dialog($localize`Include a file?`,
        $localize`The path does not end with a '${this.fileService.dirsep}' character, which means that you include a file, not a folder.\nDo you want to include the specified file?`,
        [$localize`No`, $localize`Yes`], (idx) => {
          if (idx == 1) {
            this.validateSourcePath();
          }
        });
    } else {
      this.validateSourcePath();
    }
  }

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
