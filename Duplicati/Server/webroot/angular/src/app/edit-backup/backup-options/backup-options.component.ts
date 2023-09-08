import { Input, Output } from '@angular/core';
import { EventEmitter } from '@angular/core';
import { Component } from '@angular/core';
import { Subscription } from 'rxjs';
import { ConvertService } from '../../services/convert.service';
import { ParserService } from '../../services/parser.service';
import { CommandLineArgument, ServerModuleDescription } from '../../system-info/system-info';
import { SystemInfoService } from '../../system-info/system-info.service';
import { BackupOptions } from '../backup-options';

@Component({
  selector: 'app-backup-options',
  templateUrl: './backup-options.component.html',
  styleUrls: ['./backup-options.component.less']
})
export class BackupOptionsComponent {
  private _options!: BackupOptions;
  @Input({ required: true }) set options(value: BackupOptions) {
    if (this._options !== value) {
      this._options = value;
      this.updateRetentionFromOptions();
      this.showAdvanced = value.extendedOptions.length > 0;
    }
  }
  get options(): BackupOptions {
    return this._options;
  }
  @Input() backendModule: string = '';
  @Output() optionsChange = new EventEmitter<BackupOptions>();
  @Output() prev = new EventEmitter<void>();
  @Output() save = new EventEmitter<void>();

  serverModules: ServerModuleDescription[] = [];
  extendedOptionList: CommandLineArgument[] = [];
  get extendedOptions(): string[] {
    return this.options.extendedOptions;
  }
  set extendedOptions(value: string[]) {
    if (value !== this.options.extendedOptions) {
      this._options = Object.assign(new BackupOptions(), { ...this.options, extendedOptions: value });
      this.optionsChange.emit(this.options);
    }
  }
  private _keepType: string = '';
  get keepType(): string {
    return this._keepType;
  }
  set keepType(value: string) {
    if (this._keepType !== value) {
      this._keepType = value;
      this.updateRetentionOption();
    }
  }
  fileSizeMultipliers: ({ name: string, value: string })[] = [];
  timeMultipliers: ({ name: string, value: string })[] = [
    { name: 'Days', value: 'D' },
    { name: 'Weeks', value: 'W' },
    { name: 'Months', value: 'M' },
    { name: 'Years', value: 'Y' }
  ];

  showAdvanced = false;
  showAdvancedTextArea = false;
  private subscription?: Subscription;

  get dblockSize(): string {
    return this.options.dblockSize;
  }
  set dblockSize(value: string) {
    this._options = Object.assign(new BackupOptions(), { ...this.options, dblockSize: value });
    this.optionsChange.emit(this.options);
  }

  private _retentionPolicy: string = '';
  get retentionPolicy(): string {
    return this._retentionPolicy;
  }
  set retentionPolicy(value: string) {
    this._retentionPolicy = value;
    this.updateRetentionOption();
  }
  private _keepVersions: number = 1;
  get keepVersions(): number {
    return this._keepVersions;
  }
  set keepVersions(value: number) {
    this._keepVersions = value;
    this.updateRetentionOption();
  }
  private _keepTime: string = '1D';
  get keepTime(): string {
    return this._keepTime;
  }
  set keepTime(value: string) {
    this._keepTime = value;
    this.updateRetentionOption();
  }

  constructor(public convert: ConvertService,
    public parser: ParserService,
    private systemInfo: SystemInfoService) { }

  ngOnInit() {
    this.fileSizeMultipliers = this.parser.fileSizeMultipliers;
    this.subscription = this.systemInfo.getState().subscribe(info => {
      this.extendedOptionList = this.parser.buildOptionList(info,
        this.options.encryptionModule, this.options.compressionModule, this.backendModule);
    });
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  getServerModuleSettings(name: string): string {
    let settings = this.options.serverModuleSettings?.get(name);
    return settings || '';
  }

  setServerModuleSettings(name: string, value: string) {
    this._options = Object.assign(new BackupOptions(),
      {
        ...this.options,
        serverModuleSettings: new Map<string, string>(
          [
            ...(this.options.serverModuleSettings?.entries() || []),
            [name, value]
          ]
        )
      });
    this.optionsChange.emit(this.options);
  }

  updateRetentionFromOptions() {
    if (this.options.retention == null) {
      this._keepType = '';
    } else {
      if (this.options.retention.type === 'custom') {
        this._retentionPolicy = this.options.retention.policy;
      } else if (this.options.retention.type === 'versions') {
        this._keepVersions = this.options.retention.keepVersions;
      } else if (this.options.retention.type === 'time') {
        this._keepTime = this.options.retention.keepTime;
      }
      this._keepType = this.options.retention.type;
    }
  }
  updateRetentionOption() {
    let newOptions: BackupOptions | undefined = undefined;
    if (this.keepType === '') {
      if (this.options.retention != null) {
        newOptions = Object.assign(new BackupOptions(), { ...this.options, retention: null });
      }
    } else if (this.keepType === 'custom') {
      if (this.options.retention == null || this.options.retention.type !== 'custom'
        || this.options.retention.policy !== this.retentionPolicy) {
        newOptions = Object.assign(new BackupOptions(), {
          ...this.options,
          retention: {
            type: 'custom',
            policy: this.retentionPolicy
          }
        });
      }
    } else if (this.keepType === 'time') {
      if (this.options.retention == null || this.options.retention.type !== 'time'
        || this.options.retention.keepTime !== this.keepTime) {
        newOptions = Object.assign(new BackupOptions(), {
          ...this.options,
          retention: {
            type: 'time',
            keepTime: this.keepTime
          }
        });
      }
    } else if (this.keepType === 'versions') {
      if (this.options.retention == null || this.options.retention.type !== 'versions'
        || this.options.retention.keepVersions !== this.keepVersions) {
        newOptions = Object.assign(new BackupOptions(), {
          ...this.options,
          retention: {
            type: 'versions',
            keepVersions: this.keepVersions
          }
        });
      }
    }
    if (newOptions !== undefined) {
      this._options = newOptions;
      this.optionsChange.emit(newOptions);
    }
  }
}
