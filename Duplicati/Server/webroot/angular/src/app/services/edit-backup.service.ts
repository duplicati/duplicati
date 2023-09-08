import { Inject, Injectable, InjectionToken } from '@angular/core';
import { Observable, of } from 'rxjs';
import { AddOrUpdateBackupData, Backup, BackupSetting, Schedule } from '../backup';
import { BackupOptions } from '../edit-backup/backup-options';
import { CommandLineArgument } from '../system-info/system-info';
import { ConvertService } from './convert.service';
import { FileFilterService } from './file-filter.service';
import { ParserService } from './parser.service';

// Used as a reject signal by BackupChecker to indicate that a certain step should be shown
export class RejectToStep {
  constructor(public step: number) { }
}

export interface BackupChecker {
  // Check the backup
  // Should resolve the promise if successful, display error dialog and reject the promise otherwise
  check(b: AddOrUpdateBackupData, opt: BackupOptions): Promise<void>;
}

export const BACKUP_CHECKS = new InjectionToken<BackupChecker[]>("backup checks", {
  providedIn: 'root',
  factory: () => []
});

@Injectable({
  providedIn: 'root'
})
export class EditBackupService {

  readonly smartRetention: string = '1W:1D,4W:1W,12M:1M';

  constructor(@Inject(BACKUP_CHECKS) private checks: BackupChecker[],
    private parser: ParserService,
    private filters: FileFilterService,
    private convert: ConvertService) { }

  makeBackupData(backup: Backup, schedule: Schedule | null, options: BackupOptions): AddOrUpdateBackupData {

    if (!this.preValidate(backup, schedule, options)) {
      throw new Error('PreValidate');
    }

    let result: AddOrUpdateBackupData = {
      Backup: { ...backup },
      Schedule: schedule ? { ...schedule } : null
    };

    let optionsMap = this.optionsToMap(options);

    let originalSettings = backup.Settings;
    let settings: BackupSetting[] = [];
    for (let [k, v] of Object.entries(optionsMap)) {
      let origfilter = '';
      let origarg: CommandLineArgument | null = null;
      const original = originalSettings.find(v => v.Name === k);
      if (original != null) {
        origfilter = original.Filter;
        origarg = original.Argument;
      }
      settings.push({
        Name: k,
        Value: v,
        Filter: origfilter,
        Argument: origarg
      });
    }
    result.Backup.Settings = settings;

    // Update filter string order
    for (let i = 0; i < result.Backup.Filters.length; ++i) {
      result.Backup.Filters[i].Order = i;
    }

    return result;
  }

  preValidate(backup: Backup, schedule: Schedule | null, options: BackupOptions): boolean {
    return true;
  }

  checkBackup(backup: AddOrUpdateBackupData, options: BackupOptions): Promise<void> {
    let promise = Promise.resolve();
    for (let c of this.checks) {
      promise = promise.then(() => c.check(backup, options));
    }
    return promise;
  }

  postValidate(backup: Backup, schedule: Schedule | null, options: BackupOptions): boolean {
    return true;
  }

  optionsToMap(options: BackupOptions): Record<string, any> {
    let res: Record<string, any> = {};
    if (options.excludeFileSize != null) {
      res['--skip-files-larger-than'] = this.convert.formatSizeString(options.excludeFileSize);
    }
    if (options.encryptionModule.length == 0) {
      res['--no-encryption'] = true;
    } else {
      res['encryption-module'] = options.encryptionModule;
    }
    if (options.excludeFileAttributes.length > 0) {
      // Remove duplicates
      let excludeAttributes = new Set(options.excludeFileAttributes);
      res['--exclude-files-attributes'] = [...excludeAttributes].join(',');
    }
    if (options.passphrase.length > 0) {
      res['passphrase'] = options.passphrase;
    }
    if (options.dblockSize.length > 0) {
      res['dblock-size'] = options.dblockSize;
    }
    if (options.compressionModule.length > 0) {
      res['compression-module'] = options.compressionModule;
    }
    if (options.retention != null) {
      if (options.retention.type === 'time') {
        res['keep-time'] = options.retention.keepTime;
      } else if (options.retention.type === 'versions') {
        res['keep-versions'] = options.retention.keepVersions;
      } else if (options.retention.type === 'smart') {
        res['retention-policy'] = this.smartRetention;
      } else if (options.retention.type === 'custom') {
        res['retention-policy'] = options.retention.policy;
      }
    }
    if (!this.parser.parseExtraOptions(options.extendedOptions, res)) {
      throw new Error('DuplicateOptions');
    }
    if (options.serverModuleSettings != null) {
      for (let [k, v] of options.serverModuleSettings?.entries()) {
        res['--' + k] = v;
      }
    }

    return res;
  }

  parseOptions(b: Backup): BackupOptions {
    let options = new BackupOptions();

    let optMap = new Map<string, string>();
    let extopts = new Map<string, string>();
    for (let s of b.Settings) {
      if (s.Name.startsWith('--')) {
        extopts.set(s.Name, s.Value);
      } else {
        optMap.set(s.Name, s.Value);
      }
    }
    options.passphrase = optMap.get('passphrase') ?? options.passphrase;
    options.repeatPassphrase = options.passphrase;
    options.encryptionModule = optMap.get('encryption-module') ?? options.encryptionModule;
    options.dblockSize = optMap.get('dblock-size') ?? options.dblockSize;
    options.compressionModule = optMap.get('compression-module') ?? options.compressionModule;

    let exclattr = (extopts.get('--exclude-files-attributes') || '').split(',');
    // Add known exclude attributes to options, keep the rest in advanced options
    const fileAttrs = this.filters.getFileAttributes();
    let attrs = new Set<string>();
    for (let i = exclattr.length - 1; i >= 0; i--) {
      let cmp = exclattr[i].trim().toLowerCase();
      if (cmp.length == 0) {
        exclattr.splice(i, 1);
      }
      let attr = fileAttrs.find(attr => attr.value === cmp);
      if (attr != null) {
        attrs.add(attr.value);
        exclattr.splice(i, 1);
      }
    }
    options.excludeFileAttributes.push(...attrs.values());
    if (exclattr.length == 0) {
      extopts.delete('--exclude-files-attributes')
    } else {
      extopts.set('--exclude-files-attributes', exclattr.join(','));
    }


    const keepTime = optMap.get('keep-time');
    const keepVersions = optMap.get('keep-versions');
    const retention = optMap.get('retention-policy');
    if (keepTime != null && keepTime.trim().length > 0) {
      options.retention = {
        type: 'time', keepTime: keepTime.trim()
      };
    } else if (keepVersions != null && keepVersions.trim().length > 0) {
      options.retention = {
        type: 'versions', keepVersions: parseInt(keepVersions)
      };
    } else if (retention != null && retention.trim().length > 0) {
      if (retention.trim() == this.smartRetention) {
        options.retention = { type: 'smart' };
      } else {
        options.retention = { type: 'custom', policy: retention.trim() };
      }
    }

    const skipFilesLarger = extopts.get('--skip-files-larger-than');
    if (skipFilesLarger != null) {
      options.excludeFileSize = this.parser.parseSizeString(skipFilesLarger);
    }

    let delopts = ['--skip-files-larger-than', '--no-encryption'];
    for (let opt of delopts) {
      extopts.delete(opt);
    }
    options.extendedOptions = this.parser.serializeAdvancedOptionsToArray(extopts);
    // TODO: Initialize server module settings
    // options.serverModuleSettings = new Map();
    // this.parser.extractServerModuleOptions(options.extendedOptions, this.serverModules, options.serverModuleSettings, 'SupportedLocalCommands');

    return options;
  }

  initialScheduleTime(time: string | undefined): string {
    const now = new Date();
    let date = this.convert.parseDate(time || '');
    if (isNaN(date.getTime())) {
      date = this.convert.parseDate('1970-01-01T' + time);
      if (!isNaN(date.getTime())) {
        date = new Date(now.getFullYear(), now.getMonth(), now.getDate(),
          date.getHours(), date.getMinutes(), date.getSeconds());
        if (date < now) {
          date.setDate(date.getDate() + 1);
        }
      }

      if (isNaN(date.getTime())) {
        date = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 13, 0, 0);
        if (date < now) {
          date.setDate(date.getDate() + 1);
        }
      }
    }

    return date.toISOString();
  }
}
