import { Injectable } from '@angular/core';
import { Fileset } from '../backup';
import { ConvertService } from './convert.service';

export type LabeledFileset = Fileset & { DisplayLabel: string, GroupLabel: string };

@Injectable({
  providedIn: 'root'
})
export class RestoreService {

  private temporaryFilesets = new Map<string, Fileset[]>();

  constructor(private convert: ConvertService) { }

  setTemporaryFileset(backupId: string, filesets: Fileset[] | null) {
    if (filesets == null) {
      this.temporaryFilesets.delete(backupId);
    } else {
      this.temporaryFilesets.set(backupId, filesets);
    }
  }
  clearTemporaryFilesets() {
    this.temporaryFilesets.clear();
  }
  getTemporaryFileset(backupId: string): Fileset[] | undefined {
    return this.temporaryFilesets.get(backupId);
  }

  filesetGroupLabels(): (dt: Date) => string {
    let dateStamp = (a: Date) => a.getFullYear() * 10000 + a.getMonth() * 100 + a.getDate();
    const now = new Date();
    const today = dateStamp(now);
    const yesterday = dateStamp(new Date(new Date().setDate(now.getDate() - 1)));
    const week = dateStamp(new Date(new Date().setDate(now.getDate() - 7)));
    const thismonth = dateStamp(new Date(new Date().setMonth(now.getMonth() - 1)));
    const lastmonth = dateStamp(new Date(new Date().setMonth(now.getMonth() - 2)));

    const dateBuckets = [
      { text: $localize`Today`, stamp: today },
      { text: $localize`Yesterday`, stamp: yesterday },
      { text: $localize`This week`, stamp: week },
      { text: $localize`This month`, stamp: thismonth },
      { text: $localize`Last month`, stamp: lastmonth },
    ];

    return (dt: Date) => {
      const stamp = dateStamp(dt);
      const idx = dateBuckets.findIndex(v => stamp >= v.stamp);
      if (idx >= 0) {
        return dateBuckets[idx].text;
      } else {
        return dt.getFullYear() + '';
      }
    };
  }

  parseBackupTimesData(filesets: Fileset[]): LabeledFileset[] {
    const createGroupLabel = this.filesetGroupLabels();
    return filesets.map((fileset, idx) => {
      const date = this.convert.parseDate(fileset.Time);
      let displayLabel = `${fileset.Version}: ${this.convert.toDisplayDateAndTime(date)}`;
      if (fileset.IsFullBackup === 0) {
        displayLabel += $localize` (partial)`;
      }
      const groupLabel = idx == 0 ? $localize`Latest` : createGroupLabel(date);
      return {
        ...fileset,
        DisplayLabel: displayLabel,
        GroupLabel: groupLabel
      };
    });
  }
}
