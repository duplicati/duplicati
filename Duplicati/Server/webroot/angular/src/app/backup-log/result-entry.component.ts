import { Component, Input } from '@angular/core';
import { IconService } from '../services/icon.service';
import { BackupLogEntry } from '../services/log-entry';

@Component({
  selector: 'app-result-entry',
  templateUrl: './result-entry.component.html',
  styleUrls: ['./result-entry.component.less']
})
export class ResultEntryComponent {

  @Input({ required: true }) item!: BackupLogEntry;
  expanded: boolean = false;

  constructor(public icon: IconService) { }
}
