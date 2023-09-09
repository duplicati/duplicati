import { formatDate } from '@angular/common';
import { Input } from '@angular/core';
import { Component } from '@angular/core';
import { ConvertService } from '../services/convert.service';
import { BackupLogEntry } from '../services/log-entry';

@Component({
  selector: 'app-backup-result',
  templateUrl: './backup-result.component.html',
  styleUrls: ['./backup-result.component.less']
})
export class BackupResultComponent {
  @Input({ required: true }) result!: any;
  @Input() formatted?: string;

  logExpanded: boolean = false;

  constructor(public convert: ConvertService) { }

  parseTimestampToSeconds(timestamp: number | string) {
    return formatDate(timestamp, 'YYYY-MM-dd HH:mm:ss', 'en');
  }
}
