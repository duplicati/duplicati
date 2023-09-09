import { formatDate } from '@angular/common';
import { Component, Input } from '@angular/core';
import { ConvertService } from '../../services/convert.service';
import { IconService } from '../../services/icon.service';

@Component({
  selector: 'app-phase-base',
  templateUrl: './phase-base.component.html'
})
export class PhaseBaseComponent {
  @Input({ required: true }) results: any;
  @Input({ required: true }) title!: string;

  expanded: boolean = false;

  constructor(public convert: ConvertService,
    public icon: IconService) { }

  parseTimestampToSeconds(timestamp: number | string) {
    return formatDate(timestamp, 'YYYY-MM-dd HH:mm:ss', 'en');
  }
}
