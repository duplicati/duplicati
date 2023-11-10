import { EventEmitter, Output } from '@angular/core';
import { Input } from '@angular/core';
import { Component } from '@angular/core';

@Component({
  selector: 'app-backup-filter-list',
  templateUrl: './backup-filter-list.component.html',
  styleUrls: ['./backup-filter-list.component.less']
})
export class BackupFilterListComponent {
  @Input({ required: true }) filters: string[] = [];
  @Output() filtersChange = new EventEmitter<string[]>();

  setFilter(i: number, filter: string): void {
    let filters = [...this.filters];
    filters[i] = filter;
    this.filtersChange.emit(filters);
  }

  addFilter(filter: string): void {
    let filters = [...this.filters];
    filters.push(filter);
    this.filtersChange.emit(filters);
  }

  removeFilter(i: number): void {
    let filters = [...this.filters];
    filters.splice(i, 1);
    this.filtersChange.emit(filters);
  }

  trackIndex(index: number, item: any) {
    return index;
  }
}
