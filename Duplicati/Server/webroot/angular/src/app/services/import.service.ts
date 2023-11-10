import { Injectable } from '@angular/core';
import { AddOrUpdateBackupData, Backup } from '../backup';

@Injectable({
  providedIn: 'root'
})
export class ImportService {

  // Global state to communicate between ImportComponent and EditBackupComponent
  private currentData?: Partial<AddOrUpdateBackupData>;

  constructor() { }

  resetImportData() {
    this.currentData = undefined;
  }
  setImportData(importConfig: Partial<AddOrUpdateBackupData>) {
    this.currentData = importConfig;
  }

  getImportData(): Partial<AddOrUpdateBackupData> | undefined {
    return this.currentData;
  }
}
