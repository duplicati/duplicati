import { SelectionChange, SelectionModel } from '@angular/cdk/collections';
import { FlatTreeControl } from '@angular/cdk/tree';
import { EventEmitter, Injectable, Output } from '@angular/core';
import { SimpleChanges } from '@angular/core';
import { Component, Input } from '@angular/core';
import { BehaviorSubject, Subscription } from 'rxjs';
import { FileDataSource, FileFlatNode, FileDatabase } from '../destination-folder-picker/file-data-source';
import { ConvertService } from '../services/convert.service';
import { DialogService } from '../services/dialog.service';
import { EditUriService } from '../services/edit-uri.service';
import { FileFilterService } from '../services/file-filter.service';
import { FileNode, FileService } from '../services/file.service';
import { SourceFileDatabase } from './source-file-database';

export const IncludeMarker = {
  Unchecked: '',
  Included: '+',
  Partial: ' ',
  Excluded: '-',
} as const;

export type IncludeMarkerType = typeof IncludeMarker[keyof typeof IncludeMarker];

@Component({
  selector: 'app-source-folder-picker',
  templateUrl: './source-folder-picker.component.html',
  styleUrls: ['./source-folder-picker.component.less']
})
export class SourceFolderPickerComponent {
  // Create copies of arrays, because they will be modified (that breaks change detection)
  private _sources: string[] = [];
  private _filters: string[] = [];
  @Input() set sources(v: string[]) {
    this._sources = [...v];
  }
  get sources() {
    return this._sources;
  }
  @Output() sourcesChange = new EventEmitter<string[]>();
  @Input() set filters(v: string[]) {
    this._filters = [...v];
  }
  get filters() {
    return this._filters;
  }
  @Output() filtersChange = new EventEmitter<string[]>();
  @Input() showHidden: boolean = false;
  @Input() excludeAttributes: string[] = [];
  @Input() excludeSize: number | null = null;

  treeControl = new FlatTreeControl<FileFlatNode>(node => node.level, node => node.expandable);
  private fileDatabase: SourceFileDatabase;
  dataSource: FileDataSource;
  sourceNodeChildren = new BehaviorSubject<FileFlatNode[]>([]);

  IncludeMarker = IncludeMarker;

  // Selected sources in file tree
  private sourceSelection: SelectionModel<string>;
  private includeMarkers = new Map<string, IncludeMarkerType>();
  private excludemap = new Map<string, boolean>();
  private filterList?: [boolean, RegExp][];

  private subscription?: Subscription;
  private expandSubscription?: Subscription;

  constructor(private fileService: FileService,
    private dialog: DialogService,
    private filterService: FileFilterService,
    private convert: ConvertService) {
    this.fileDatabase = new SourceFileDatabase(false, this.fileService, this.filterService);
    this.dataSource = new FileDataSource(this.treeControl, this.fileService, this.fileDatabase, this.dialog.connectionError($localize`Failed to load files: `));
    this.fileDatabase.setSourceNodeChildren(this.sourceNodeChildren);
    this.sourceSelection = new SelectionModel<string>(true, [], true, (p1, p2) => this.fileService.pathsEqual(p1, p2));
  }

  ngOnInit() {
    this.filterService.loadFilterGroups().subscribe();
    this.dataSource.data = this.fileDatabase.initialData(false).concat([]);
    this.dataSource.data.forEach(n => {
      if (n.level === 0) {
        this.treeControl.expand(n);
      }
    });
    this.subscription = this.sourceSelection.changed.subscribe(change => this.selectionChanged(change));
    this.sourceSelection.setSelection(...this.sources);
  }

  ngOnChanges(changes: SimpleChanges) {
    let change = Object.keys(changes).find(c => c === 'sources' || c === 'filters' || c === 'excludeAttributes' || c === 'excludeSize');
    if (change != null && !changes[change].isFirstChange()) {
      this.syncTreeWithList();
    }
    if ('showHidden' in changes) {
      this.dataSource.showHidden(this.showHidden);
    }
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
    this.expandSubscription?.unsubscribe();
    this.sourceNodeChildren.complete();
  }

  private selectionChanged(change: SelectionChange<string>): void {
    if (change.added) {
      this.sourceNodeChildren.value.push(...change.added.map(s => this.createSourceNode(s)));
    }
    if (change.removed) {
      for (const s of change.removed) {
        const idx = this.sourceNodeChildren.value.findIndex(n => n.node.id != null && this.fileService.pathsEqual(s, n.node.id));
        if (idx >= 0) {
          this.sourceNodeChildren.value.splice(idx, 1);
        }
      }
    }
    if (change.added || change.removed) {
      this.sourceNodeChildren.next(this.sourceNodeChildren.value);
    }
  }
  private createSourceNode(s: string): FileFlatNode {
    let n: FileFlatNode = new FileFlatNode({
      text: this.filterService.getSourceDisplayName(s),
      id: s,
      root: false,
      iconCls: 'x-tree-icon-folder'
    }, 1, false);

    this.fileService.setIconCls(n.node);

    this.fileService.checkDefunct(n.node).subscribe(v => {
      if (v) {
        n.node.iconCls = 'x-tree-icon-broken';
        if (this.sourceNodeChildren.value.includes(n)) {
          this.sourceNodeChildren.next(this.sourceNodeChildren.value);
        }
      }
    });
    return n;
  }

  shouldIncludeNode(node: FileFlatNode, checkSize?: boolean): boolean {
    if (node.node.id == null) {
      return false;
    }
    if (checkSize === undefined) {
      checkSize = true;
    }
    // ExcludeSize overrides source paths
    if (checkSize && this.filterService.isExcludedBySize(node.node, this.excludeSize)) {
      return false;
    }
    const cp = this.fileService.comparablePath(node.node.id);

    // Check if explicitly included in sources
    if (this.sourceSelection.isSelected(cp)) {
      return true;
    }

    // Result is true if included, false if excluded, null if none match
    var result: boolean | null = null;

    // Check filter expression
    if (this.filterList == null)
      result = this.excludemap.get(cp) ? false : null;
    else {
      result = this.filterService.evalFilter(node.node.id, this.filterList, null);
    }

    if (result !== false && this.filterService.isExcludedByAttributes(node.node, this.excludeAttributes)) {
      result = false;
    }

    if (result === null) {
      // Include by default
      result = true;
    }
    return result;
  }

  getIncludeMarker(node: FileFlatNode | null): IncludeMarkerType {
    if (node == null || node.node.root || node.node.id == null) {
      // Root nodes have no check box
      return IncludeMarker.Unchecked;
    }
    if (this.sourceNodeChildren.value.includes(node)) {
      // Node is under source files node, always checked
      return IncludeMarker.Included;
    } else {
      let cp = this.fileService.comparablePath(node.node.id);
      let m = this.includeMarkers.get(cp);
      if (m !== undefined) {
        return m;
      } else {
        if (this.sourceSelection.isSelected(cp)) {
          if (!this.filterService.isExcludedBySize(node.node, this.excludeSize)) {
            return IncludeMarker.Included;
          } else {
            return IncludeMarker.Excluded;
          }
        }
        const parent = this.dataSource.getParentNode(node);
        if (parent != null) {
          const parentInclude = this.getIncludeMarker(parent);
          if (parentInclude === IncludeMarker.Included) {
            return this.shouldIncludeNode(node) ? IncludeMarker.Included : IncludeMarker.Excluded;
          } else if (parentInclude === IncludeMarker.Excluded) {
            return IncludeMarker.Excluded;
          }
        }
      }
    }
    return IncludeMarker.Unchecked;
  }

  getIncludeLabel(node: FileFlatNode): string {
    let include = this.getIncludeMarker(node);
    if (include === IncludeMarker.Partial) {
      return $localize`partially included`;
    } else if (include === IncludeMarker.Excluded) {
      return $localize`excluded`;
    } else if (include === IncludeMarker.Included) {
      return $localize`included`;
    }
    return $localize`not checked`;
  }

  updateFilterList(): void {
    ({ excludemap: this.excludemap, filterList: this.filterList } = this.filterService.buildExcludeMap(this.filters));
    this.dataSource.dataChange.next(this.dataSource.data);
  }

  syncTreeWithList(): void {
    this.updateFilterList();
    this.includeMarkers.clear();

    this.sourceSelection.setSelection(...this.sources.filter(s => s.length > 0));

    this.filterService.buildPartialIncludeMap(this.sources).forEach((value, key) => {
      if (value) {
        this.includeMarkers.set(key, IncludeMarker.Partial);
      }
    });
    this.dataSource.dataChange.next(this.dataSource.data);
  }

  hasChild(_: number, node: FileFlatNode): boolean {
    return node.expandable;
  }

  toggleChecked(node: FileFlatNode): void {
    if (node.node.root || node.node.id == null) {
      return;
    }
    const path = node.node.id;
    const includeMarker = this.getIncludeMarker(node);
    let filtersChanged = false;
    let sourcesChanged = false;
    if (includeMarker === IncludeMarker.Unchecked || includeMarker === IncludeMarker.Partial) {
      this.filterService.insertSourceDirectory(path, this.sources);

      this.sourceSelection.setSelection(...this.sources);
      sourcesChanged = true;
    } else if (includeMarker === IncludeMarker.Included) {
      if (this.sourceSelection.isSelected(path)) {
        filtersChanged = this.filterService.removePathFromArray(this.sources, path);
        this.sourceSelection.deselect(path);
        // If children of the source were excluded, those filters are not needed any more
        if (this.filterService.removeExcludeFiltersOfChildren(this.filters, path)) {
          filtersChanged = true;
        }
        sourcesChanged = true;
      } else {
        // Remove explicit include filters, if present
        filtersChanged = this.filterService.removePathFromArray(this.filters, '+' + path);
        this.updateFilterList();
        if (this.shouldIncludeNode(node, false)) {
          // No explicit include filter, add exclude filter to start of list
          this.filters.unshift('-' + path);
          filtersChanged = true;
        }
      }
    } else if (includeMarker === IncludeMarker.Excluded) {
      filtersChanged = this.filterService.removePathFromArray(this.filters, '-' + path);
      this.updateFilterList();
      if (this.filterService.isExcludedByAttributes(node.node, this.excludeAttributes)
        && this.sources.findIndex(p => this.fileService.pathsEqual(p, path)) == -1) {
        // Node is excluded by attributes, have to add as source to override
        this.sources.push(path);
        sourcesChanged = true;
      } else if (!this.shouldIncludeNode(node, false)) {
        // No explicit exclude filter, add include filter to start of list
        this.filters.unshift('+' + path);
        filtersChanged = true;
      }
      if (this.filterService.isExcludedBySize(node.node, this.excludeSize)) {
        this.dialog.dialog($localize`Cannot include "${node.node.text}"`,
          $localize`The file size is ${this.convert.formatSizeString(node.node.fileSize || 0)}, larger than the maximum specified size. If the file size decreases, it will be included in future backups.`);
      }
    }

    if (filtersChanged) {
      this.filtersChange.emit(this.filters);
    }
    if (sourcesChanged) {
      this.sourcesChange.emit(this.sources);
    }
  }

  doubleClick(node: FileFlatNode): void {
    if (this.sourceNodeChildren.value.includes(node) && node.node.id != null) {
      // Double click on node to open folder
      this.expandPath(node.node.id);
    }
  }

  private expandPath(path: string): void {
    this.expandSubscription?.unsubscribe();


    let longestPrefix = '';

    const cp = this.fileService.comparablePath(path);

    let checkNode = (n: FileFlatNode) => {
      if (!n.invisible && n.node.root) {
        if (this.fileDatabase.getRootNode(cp) === n) {
          return true;
        }
      } else if (this.sourceNodeChildren.value.includes(n)) {
        return false;
      } else if (!n.invisible && n.node.id != null) {
        const nodePath = this.fileService.comparablePath(n.node.id);
        if (nodePath === cp) {
          longestPrefix = cp;
        } else if (cp.startsWith(nodePath)) {
          if (longestPrefix.length < nodePath.length) {
            longestPrefix = nodePath;
          }
          return true;
        }
      }
      return false;
    }
    // Data array might grow during iteration, so need to use index-based access
    // This is safe because children are always inserted after parents, but some nodes might be visited multiple times
    for (let i = 0; i < this.dataSource.data.length; ++i) {
      const n = this.dataSource.data[i];
      if (checkNode(n)) {
        this.treeControl.expand(this.dataSource.data[i]);
      }
    }
    if (longestPrefix !== cp) {
      // Need to wait until child nodes are loaded
      this.expandSubscription = this.dataSource.dataChange.subscribe(data => {
        const idx = data.findIndex(n => n.node.id != null && this.fileService.pathsEqual(n.node.id, longestPrefix));
        if (idx < 0 || !this.treeControl.isExpanded(data[idx])) {
          // User has closed node, cancel expanding children
          this.expandSubscription?.unsubscribe();
          return;
        }
        // Expand next level if loaded (this will trigger another dataChange)
        const curLevel = data[idx].level;
        for (let i = idx + 1; i < data.length && data[i].level > curLevel; ++i) {
          const n = data[i];
          if (checkNode(n)) {
            this.treeControl.expand(n);
            break;
          }
        }
        if (longestPrefix === cp) {
          this.expandSubscription?.unsubscribe();
          this.navigateToPath(path);
        }
      });
    } else {
      this.navigateToPath(path);
    }
  }

  navigateToPath(path: string): void {
    let e = document.getElementById('node-' + path);
    if (e != null) {
      e.scrollIntoView();
    }
  }
}
