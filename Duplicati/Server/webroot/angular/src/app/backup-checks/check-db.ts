import { Injectable } from "@angular/core";
import { AddOrUpdateBackupData } from "../backup";
import { BackupOptions } from "../edit-backup/backup-options";
import { DialogService } from "../services/dialog.service";
import { BackupChecker } from "../services/edit-backup.service";
import { RemoteOperationService } from "../services/remote-operation.service";

@Injectable()
export class CheckForExistingDb implements BackupChecker {
  constructor(private remoteOperation: RemoteOperationService,
    private dialog: DialogService) { }

  check(b: AddOrUpdateBackupData, opt: BackupOptions): Promise<void> {
    if (!opt.isNew) {
      return Promise.resolve();
    }
    return this.remoteOperation.locateDbUri(b.Backup.TargetURL)
      .pipe()
      .toPromise().then(
        (resp) => {
          if (resp?.Exists && resp.Path != null) {
            return new Promise((resolve, reject) => {
              this.dialog.dialog('Use existing database?',
                'An existing local database for the storage has been found.\nRe-using the database will allow the command-line and server instances to work on the same remote storage.\n\n Do you wish to use the existing database?',
                ['Cancel', 'Yes', 'No'],
                (ix) => {
                  if (ix == 2) {
                    b.Backup.DBPath = resp.Path!;
                  }

                  if (ix == 1 || ix == 2) {
                    resolve();
                  }
                  else {
                    reject();
                  }
                })
            });
          }
          return Promise.resolve();
        },
        (error) => {
          this.dialog.connectionError('Connection error', error);
          return Promise.reject(error);
        }
      );
  }
}
