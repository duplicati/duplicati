import { EventEmitter, Input, Output } from '@angular/core';
import { Component } from '@angular/core';

@Component({
  selector: 'app-backup-filters',
  templateUrl: './backup-filters.component.html',
  styleUrls: ['./backup-filters.component.less']
})
export class BackupFiltersComponent {
  @Input({ required: true }) filters: string[] = [];
  @Output() filtersChange = new EventEmitter<string[]>();
}
