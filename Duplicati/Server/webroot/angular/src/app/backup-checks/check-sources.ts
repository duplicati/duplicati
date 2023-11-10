import { Injectable } from "@angular/core";
import { AddOrUpdateBackupData } from "../backup";
import { BackupOptions } from "../edit-backup/backup-options";
import { DialogService } from "../services/dialog.service";
import { BackupChecker, RejectToStep } from "../services/edit-backup.service";

@Injectable()
export class CheckSources implements BackupChecker {

  constructor(private dialog: DialogService) { }

  check(b: AddOrUpdateBackupData, opt: BackupOptions): Promise<void> {
    if (b.Backup.Sources == null || b.Backup.Sources.length == 0) {
      this.dialog.dialog($localize`Missing sources`, $localize`You must choose at least one source folder`);
      return Promise.reject(new RejectToStep(2));
    }
    return Promise.resolve();
  }
}
