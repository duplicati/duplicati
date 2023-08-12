import { FlatTreeControl, NestedTreeControl } from '@angular/cdk/tree';
import { SimpleChanges } from '@angular/core';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { MatTreeFlattener, MatTreeNestedDataSource } from '@angular/material/tree';
import { DialogService } from '../services/dialog.service';
import { FileNode, FileService } from '../services/file.service';
import { FileDataSource, FileFlatNode, FolderDatabase } from './file-data-source';

@Component({
  selector: 'app-destination-folder-picker',
  templateUrl: './destination-folder-picker.component.html',
  styleUrls: ['./destination-folder-picker.component.less']
})
export class DestinationFolderPickerComponent {
  @Input() showHidden: boolean = false;
  @Input() path: string = '';
  @Output() pathChange = new EventEmitter<string>();
  @Input() hideUserNode: boolean = false;

  treeControl = new FlatTreeControl<FileFlatNode>(node => node.level, node => node.expandable);
  dataSource: FileDataSource;

  constructor(private fileService: FileService,
    private folderDatabase: FolderDatabase,
    private dialog: DialogService) {
    this.dataSource = new FileDataSource(this.treeControl, this.fileService, folderDatabase);
  }

  ngOnInit() {
    this.dataSource.data = this.folderDatabase.initialData(this.hideUserNode);
    this.dataSource.data.forEach(n => this.dataSource.toggleNode(n, true));
  }

  ngOnChanges(changes: SimpleChanges) {
    if ('path' in changes) {
      this.dataSource.setSelected(this.path);
    }
    if ('showHidden' in changes) {
      this.dataSource.showHidden(this.showHidden);
    }
  }

  hasChild(_: number, node: FileFlatNode): boolean {
    return node.expandable;
  }
  isInvisible(_: number, node: FileFlatNode): boolean {
    return node.invisible;
  }

  toggleSelected(node: FileFlatNode): void {
    if (node.node.id != null) {
      this.dataSource.setSelected(node);
      this.path = node.node.id;
      this.pathChange.emit(this.path);
    }
  }

}
