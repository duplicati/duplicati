import { BACKUP_CHECKS } from "../services/edit-backup.service";
import { CheckForExistingDb } from "./check-db";
import { CheckForChangedPassphrase, CheckForDisabledEncryption, CheckForGeneratedPassphrase, CheckForMatchingPassphrase, WarnWeakPassphrase } from "./check-passphrase";
import { CheckSources } from "./check-sources";

export const backupCheckerProviders = [
  { provide: BACKUP_CHECKS, multi:true, useClass: CheckForMatchingPassphrase},
  { provide: BACKUP_CHECKS, multi: true, useClass: CheckSources },
  { provide: BACKUP_CHECKS, multi: true, useClass: CheckForGeneratedPassphrase },
  { provide: BACKUP_CHECKS, multi: true, useClass: CheckForChangedPassphrase },
  { provide: BACKUP_CHECKS, multi: true, useClass: CheckForDisabledEncryption },
  { provide: BACKUP_CHECKS, multi: true, useClass: WarnWeakPassphrase },
  { provide: BACKUP_CHECKS, multi: true, useClass: CheckForExistingDb },
];
