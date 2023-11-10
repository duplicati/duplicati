import { EventEmitter, Input, Output } from '@angular/core';
import { Component } from '@angular/core';
import { FileFilterService } from '../../services/file-filter.service';
import { FileService } from '../../services/file.service';

@Component({
  selector: 'app-backup-filter',
  templateUrl: './backup-filter.component.html',
  styleUrls: ['./backup-filter.component.less']
})
export class BackupFilterComponent {
  private _filter: string = '';
  @Input({ required: true }) set filter(v: string) {
    if (this._filter !== v) {
      this.parseFilter(v);
    }
    this._filter = v;
  }
  get filter() {
    return this._filter;
  }
  @Output() filterChange = new EventEmitter<string>();
  @Output() remove = new EventEmitter<void>();

  filterType: string = '';
  filterBody: string = '';
  filterClasses: ({ key: string, name: string })[] = [];
  filterGroups: ({ name: string, value: string })[] = [];

  selectedGroups: string[] = [];

  constructor(private filterService: FileFilterService, private fileService: FileService) { }

  ngOnInit() {
    // Re-parse filter when dirsep is ready
    this.fileService.whenDirsepReady().subscribe(() => {
      this.filterClasses = this.filterService.getFilterClasses();
      this.parseFilter(this.filter);
    });
    this.filterGroups = this.filterService.getFilterGroups();
  }

  isFilterGroup(): boolean {
    return this.filter.startsWith('-{') || this.filter.startsWith('+{');
  }

  private updateFilter(reparse?: boolean): void {
    // Save filter, because change event re-triggers input event
    this._filter = this.filterService.buildFilter(this.filterType, this.filterBody);
    if (reparse) {
      this.parseFilter(this._filter);
    }
    this.filterChange.emit(this._filter);
  }

  setFilterType(type: string): void {
    this.filterType = type;
    this.updateFilter();
  }

  setFilterBody(body: string): void {
    this.filterBody = body;
    this.selectedGroups = body.split(',');
    // If new body would create a different filter type, switch type
    this.updateFilter(true);
  }

  setFilterGroups(groups: string[]):void {
    this.selectedGroups = groups;
    this.filterBody = groups.join(',');
    this.updateFilter();
  }

  private parseFilter(f: string): void {
    let res = this.filterService.splitFilterIntoTypeAndBody(f);
    const oldBody = this.filterBody;
    if (res) {
      this.filterType = res.type;
      this.filterBody = res.body;
    } else {
      this.filterType = '';
      this.filterBody = '';
    }
    if (this.filterBody !== oldBody) {
      this.selectedGroups = this.filterBody.split(',');
    }
  }
}
