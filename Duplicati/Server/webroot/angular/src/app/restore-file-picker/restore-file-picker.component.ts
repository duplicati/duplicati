import { SelectionModel } from '@angular/cdk/collections';
import { FlatTreeControl } from '@angular/cdk/tree';
import { Component, EventEmitter, Input, Output, SimpleChanges } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ListFile } from '../backup';
import { FileDataSource, FileFlatNode } from '../destination-folder-picker/file-data-source';
import { BackupService } from '../services/backup.service';
import { ConvertService } from '../services/convert.service';
import { DialogService } from '../services/dialog.service';
import { FileFilterService } from '../services/file-filter.service';
import { FileNode, FileService } from '../services/file.service';
import { IncludeMarker, IncludeMarkerType } from '../source-folder-picker/source-folder-picker.component';
import { RestoreFileDatabase, SearchTreeNode } from './restore-file-database';

@Component({
  selector: 'app-restore-file-picker',
  templateUrl: './restore-file-picker.component.html',
  styleUrls: ['./restore-file-picker.component.less', '../source-folder-picker/source-folder-picker.component.less']
})
export class RestoreFilePickerComponent {
  @Input() sources: ListFile[] = [];
  @Input({ required: true }) backupId!: string;
  @Input({ required: true }) timestamp?: string;
  @Input() selected: string[] = [];
  @Output() selectedChange = new EventEmitter<string[]>();
  @Input() searchFilter: string = '';
  @Input() searchNodes: SearchTreeNode[] | null = [];

  get searchMode(): boolean {
    return this.searchNodes != null;
  }

  treeControl = new FlatTreeControl<FileFlatNode>(node => node.level, node => node.expandable);
  private fileDatabase: RestoreFileDatabase;
  dataSource: FileDataSource;

  selection: SelectionModel<string>;
  searchRegexp: RegExp | null = null;

  IncludeMarker = IncludeMarker;

  constructor(private fileService: FileService,
    private dialog: DialogService,
    private convert: ConvertService,
    private backupService: BackupService,
    private filterService: FileFilterService) {
    this.fileDatabase = new RestoreFileDatabase(this.fileService, this.filterService, path => this.fetchBackupFiles(path));
    this.dataSource = new FileDataSource(this.treeControl, this.fileService, this.fileDatabase, this.dialog.connectionError('Failed to load files: '));
    this.selection = new SelectionModel<string>(true, [], true, (p1, p2) => this.fileService.pathsEqual(p1, p2));
  }

  ngOnInit() {
    this.selection.changed.subscribe(() => this.selectedChange.emit([...this.selection.selected]));
  }

  ngOnChanges(changes: SimpleChanges) {
    if ('sources' in changes || 'timestamp' in changes || 'backupId' in changes || 'searchNodes' in changes) {
      this.updateRoots();
    }
    if ('selected' in changes) {
      this.selection.setSelection(...this.selected);
    }
    if ('searchFilter' in changes) {
      if (this.searchFilter) {
        this.searchRegexp = new RegExp(this.convert.globToRegexp(this.searchFilter), 'gi');
      } else {
        this.searchRegexp = null;
      }
    }
  }

  updateRoots() {
    if (this.searchNodes == null) {
      this.fileDatabase.setRoots(this.sources);
    } else {
      this.fileDatabase.setSearchNodes(this.searchNodes);
    }
    this.dataSource.data = this.fileDatabase.initialData(false);
    if (this.searchMode) {
      // Need to update this for expandAll to work
      this.treeControl.dataNodes = this.fileDatabase.getAllCachedNodes();
      this.treeControl.expandAll();
    } else {
      this.treeControl.dataNodes = [];
      this.treeControl.collapseAll();
      this.dataSource.data.forEach(n => {
        if (n.level === 0 || this.searchMode) {
          this.treeControl.expand(n);
        }
      });
    }
  }

  fetchBackupFiles(path: string): Observable<ListFile[]> {
    return this.backupService.searchFiles(this.backupId, path, this.timestamp || '', {
      prefixOnly: false,
      folderContents: true,
      exactMatch: true
    }).pipe(
      map(v => v.Files)
    );
  }

  getIncludeMarker(node: FileFlatNode | null): IncludeMarkerType {
    if (node == null || node.node.id == null) {
      return IncludeMarker.Unchecked;
    }

    if (this.selection.isSelected(node.node.id)) {
      return IncludeMarker.Included;
    } else {
      const path = node.node.id;
      // Check if parent included
      const parentPath = this.selection.selected.find(selected =>
        selected.endsWith(this.fileDatabase.dirsep)
        && this.fileService.comparablePath(path).startsWith(this.fileService.comparablePath(selected))
      );
      if (parentPath !== undefined) {
        return IncludeMarker.Included;
      }
      if (path.endsWith(this.fileDatabase.dirsep)) {
        // Check if partially included
        const childPath = this.selection.selected.find(selected =>
          this.fileService.comparablePath(selected).startsWith(this.fileService.comparablePath(path))
        );
        if (childPath !== undefined) {
          return IncludeMarker.Partial;
        }
      }
    }

    return IncludeMarker.Unchecked;
  }

  getIncludeLabel(node: FileFlatNode): string {
    let include = this.getIncludeMarker(node);
    if (include === IncludeMarker.Partial) {
      return 'partially included';
    } else if (include === IncludeMarker.Excluded) {
      return 'excluded';
    } else if (include === IncludeMarker.Included) {
      return 'included';
    }
    return 'not checked';
  }

  hasChild(_: number, node: FileFlatNode): boolean {
    return node.expandable;
  }

  isSelected(node: FileFlatNode): boolean {
    if (node.node.id == null) {
      return false;
    }
    return this.selection.isSelected(this.fileService.comparablePath(node.node.id));
  }

  toggleChecked(node: FileFlatNode): void {
    if (node.node.id == null) {
      return;
    }
    if (node.node.id != null) {
      if (this.getIncludeMarker(node) != IncludeMarker.Included) {
        // Check if all children are checked and propagate up
        let parent: FileFlatNode | null = this.dataSource.getParentNode(node) ?? node;
        let cur = node;
        let selected = [...this.selection.selected];
        while (parent != null) {
          const cp = this.fileService.comparablePath(cur.node.id!);
          const isDir = cp.endsWith(this.fileDatabase.dirsep);

          for (let i = selected.length - 1; i >= 0; i--) {
            const n = this.fileService.comparablePath(selected[i]);
            if (n == cp || (isDir && n.startsWith(cp))) {
              // Deselect child node
              selected.splice(i, 1);
            }
          }
          // Check if all children are selected
          const children = this.dataSource.getExpandedChildren(parent);
          const all = children.every(child => child.node.id === cur.node.id
            || child.node.id == null
            || this.selection.isSelected(child.node.id));

          if ((!all || parent == node || this.searchMode) && cur.node.id != null) {
            selected.push(cur.node.id);
            break;
          }
          cur = parent;
          parent = this.dataSource.getParentNode(cur);

          if (parent == null && all && !this.searchMode && cur.node.id != null) {
            selected.push(cur.node.id);
          }
        }
        this.selection.setSelection(...selected);
      } else {
        // Remove highest selected parent
        let backtrace = [];
        let p: FileFlatNode | null = node;
        while (p != null && !this.selection.isSelected(p.node.id!)) {
          backtrace.push(p);
          p = this.dataSource.getParentNode(p);
        }
        if (p != null) {
          this.selection.deselect(p.node.id!);
        }

        while (backtrace.length > 0) {
          let t = backtrace.pop()!;
          let children = p != null ? this.dataSource.getExpandedChildren(p) : this.dataSource.getRootNodes();
          for (let child of children) {
            if (t !== child) {
              this.selection.select(child.node.id!);
            }
          }
          p = t;
        }
      }
    }
  }
}
