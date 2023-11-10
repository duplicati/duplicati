import { Component, Input } from '@angular/core';
import { LiveLogEntry, RemoteLogEntry, ServerLogEntry } from '../services/log-entry';

@Component({
  selector: 'app-log-entry',
  templateUrl: './log-entry.component.html',
  styleUrls: ['./log-entry.component.less']
})
export class LogEntryComponent {

  @Input() item?: ServerLogEntry;
  @Input() itemLive?: LiveLogEntry;
  @Input() itemRemote?: RemoteLogEntry
  expanded: boolean = false;
}
