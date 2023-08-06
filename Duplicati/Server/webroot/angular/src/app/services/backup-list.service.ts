import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable, of } from 'rxjs';
import { combineLatest } from 'rxjs/operators';
import { AddOrUpdateBackupData } from '../backup';
import { ScheduleEntry, ServerStatus } from './server-status';
import { ServerStatusService } from './server-status.service';

@Injectable({
  providedIn: 'root'
})
export class BackupListService {

  constructor(private client: HttpClient, private serverStatus: ServerStatusService) { }

  getBackups(): Observable<AddOrUpdateBackupData[]> {
    return this.client.get<AddOrUpdateBackupData[]>('/backups').pipe(
      combineLatest(this.serverStatus.getProposedSchedule()),
      map(v => this.updateNextRunStamp(v[0], v[1])));
  }

  getBackupsLookup(): Observable<Record<string, AddOrUpdateBackupData>> {
    return this.getBackups().pipe(map(
      backups => {
        let result: Record<string, AddOrUpdateBackupData> = {};
        for (const b of backups) {
          result[b.Backup.ID] = b;
        }
        return result;
      }));
  }

  lookupSchedule(backup: AddOrUpdateBackupData, schedule: ScheduleEntry[]): ScheduleEntry | undefined {
    return schedule.find(s => s.Item1 == backup.Backup.ID);
  }

  private updateNextRunStamp(backups: AddOrUpdateBackupData[], schedule: ScheduleEntry[]): AddOrUpdateBackupData[] {

    for (let b of backups) {
      delete b.Backup.Metadata['NextScheduledRun'];
    }

    const backupIds = backups.map(b => b.Backup.ID);

    for (let s of schedule) {
      const idx = backupIds.indexOf(s.Item1);
      if (idx !== -1) {
        backups[idx].Backup.Metadata['NextScheduledRun'] = s.Item2;
      }
    }

    return backups;
  }
}
