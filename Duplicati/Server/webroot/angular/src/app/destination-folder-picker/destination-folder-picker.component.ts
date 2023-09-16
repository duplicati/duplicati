import { SelectionModel } from '@angular/cdk/collections';
import { FlatTreeControl, NestedTreeControl } from '@angular/cdk/tree';
import { SimpleChanges } from '@angular/core';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { MatTreeFlattener, MatTreeNestedDataSource } from '@angular/material/tree';
import { DialogService } from '../services/dialog.service';
import { FileNode, FileService } from '../services/file.service';
import { FileDataSource, FileFlatNode, FileDatabase } from './file-data-source';

@Component({
  selector: 'app-destination-folder-picker',
  templateUrl: './destination-folder-picker.component.html',
  styleUrls: ['./destination-folder-picker.component.less', '../source-folder-picker/source-folder-picker.component.less']
})
export class DestinationFolderPickerComponent {
  @Input() showHidden: boolean = false;
  @Input() path: string = '';
  @Output() pathChange = new EventEmitter<string>();
  @Input() hideUserNode: boolean = false;

  treeControl = new FlatTreeControl<FileFlatNode>(node => node.level, node => node.expandable);
  private fileDatabase: FileDatabase;
  dataSource: FileDataSource;

  selection = new SelectionModel<string>(false);

  constructor(private fileService: FileService,
    private dialog: DialogService) {
    this.fileDatabase = new FileDatabase(true, this.fileService);
    this.dataSource = new FileDataSource(this.treeControl, this.fileService, this.fileDatabase, this.dialog.connectionError('Failed to load files: '));
  }

  ngOnInit() {
    this.dataSource.data = this.fileDatabase.initialData(this.hideUserNode);
    this.dataSource.data.forEach(n => {
      if (n.level === 0) {
        this.treeControl.expand(n);
      }
    });
  }

  ngOnChanges(changes: SimpleChanges) {
    if ('path' in changes) {
      this.selection.select(this.fileService.comparablePath(this.path));
    }
    if ('showHidden' in changes) {
      this.dataSource.showHidden(this.showHidden);
    }
    if ('hideUserNode' in changes) {
      if (!changes['hideUserNode'].isFirstChange()) {
        // Have to re-initialize tree if changed
        this.dataSource.data = this.fileDatabase.initialData(this.hideUserNode);
        this.dataSource.data.forEach(n => {
          if (n.level === 0) {
            this.treeControl.expand(n);
          }
        });
      }
    }
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

  toggleSelected(node: FileFlatNode): void {
    if (node.node.id != null) {
      this.selection.toggle(this.fileService.comparablePath(node.node.id));
      this.path = node.node.id;
      this.pathChange.emit(this.path);
    }
  }
}
