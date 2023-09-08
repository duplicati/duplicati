import { Injectable } from "@angular/core";
import { AddOrUpdateBackupData } from "../backup";
import { BackupOptions } from "../edit-backup/backup-options";
import { DialogService } from "../services/dialog.service";
import { BackupChecker, RejectToStep } from "../services/edit-backup.service";

@Injectable()
export class CheckRetention implements BackupChecker {

  constructor(private dialog: DialogService) { }

  check(b: AddOrUpdateBackupData, opt: BackupOptions): Promise<void> {
    if (opt.retention?.type === 'custom') {
      const valid_chars = /^((\d+[smhDWMY]|U):(\d+[smhDWMY]|U),?)+$/;
      const valid_commas = /^(\d*\w:\d*\w,)*\d*\w:\d*\w$/;
      const retentionPolicy = opt.retention.policy;
      if (retentionPolicy.indexOf(':') <= 0 || valid_chars.test(retentionPolicy) || valid_commas.test(retentionPolicy) == false) {
        this.dialog.dialog('Invalid retention time', 'You must enter a valid retention policy string');
        return Promise.reject(new RejectToStep(4));
      }
    }
    return Promise.resolve();
  }
}
