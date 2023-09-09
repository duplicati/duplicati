import { Injectable } from '@angular/core';
import { Backup } from '../backup';

@Injectable({
  providedIn: 'root'
})
export class ImportService {

  // Global state to communicate between ImportComponent and EditBackupComponent
  private currentData?: Partial<Backup>;

  constructor() { }

  resetImportData() {
    this.currentData = undefined;
  }
  setImportData(importConfig: Partial<Backup>) {
    this.currentData = importConfig;
  }

  getImportData(): Partial<Backup> | undefined {
    return this.currentData;
  }
}
