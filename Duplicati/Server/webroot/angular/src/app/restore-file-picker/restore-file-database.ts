import { MatTreeFlattener } from "@angular/material/tree";
import { BehaviorSubject, map, Observable, of, Subscription } from "rxjs";
import { ListFile } from "../backup";
import { FileDatabase, FileFlatNode } from "../destination-folder-picker/file-data-source";
import { BackupService } from "../services/backup.service";
import { FileFilterService } from "../services/file-filter.service";
import { FileNode, FileService } from "../services/file.service";

export interface SearchTreeNode {
  File: ListFile;
  Children?: SearchTreeNode[];
}

export class RestoreFileDatabase extends FileDatabase {

  private _dirsep?: string;
  private searchNodes?: SearchTreeNode[];

  get dirsep(): string {
    return this._dirsep || this.fileService.dirsep;
  }

  constructor(fileService: FileService, private filterService: FileFilterService, private fetch: (path: string) => Observable<ListFile[]>) {
    super(false, fileService);
    this.rootLevelNodes = [];
  }

  setRoots(rootPaths: ListFile[]) {
    if (rootPaths.length == 0) {
      // Use default dirsep
      this._dirsep = undefined;
    } else {
      this._dirsep = this.fileService.guessDirsep(rootPaths[0].Path);
    }
    this.searchNodes = undefined;
    this.cachedNodes.clear();
    this.rootLevelNodes = rootPaths.map(path => {
      return this.initializeNode(this.createFileNode(path));
    });
  }

  setSearchNodes(searchNodes: SearchTreeNode[]) {
    this.cachedNodes.clear();
    this.searchNodes = searchNodes;
    this.rootLevelNodes = searchNodes.map(n => this.initializeNode(this.createFileNode(n.File)));

    // Fill cache with search data
    let q = [...searchNodes];
    let flatNodes = [...this.rootLevelNodes];
    while (q.length > 0) {
      let parent = q.pop()!;
      let parentNode = flatNodes.pop()!;
      if (parent.Children != null) {
        let childNodes = parent.Children.map(n => {
          let res = this.initializeNode(this.createFileNode(n.File), parentNode);
          res.expandable = n.Children != null && n.Children.length > 0;
          return res;
        });
        this.cachedNodes.set(parent.File.Path, new BehaviorSubject<FileFlatNode[] | undefined>(childNodes));
        q.push(...parent.Children);
        flatNodes.push(...childNodes);
      }
    }
  }

  private createFileNode(file: ListFile, root?: boolean): FileNode {
    let disp = file.Path;
    let leaf = true;
    if (disp.endsWith(this.dirsep)) {
      disp = disp.slice(0, -1);
      leaf = false;
    }
    return {
      text: disp,
      root: root ?? false,
      id: file.Path,
      fileSize: file.Sizes[0],
      iconCls: leaf ? 'x-tree-icon-leaf' : '',
      leaf: leaf
    };
  }

  override initialData(hideUserNode?: boolean): FileFlatNode[] {
    return this.rootLevelNodes;
  }

  override fetchChildren(path: string): Observable<FileNode[]> {
    if (this.searchNodes != null) {
      return of([]);
    }
    return this.fetch(path).pipe(map(
      files => files.map(file => this.createFileNode(file))
    ));
  }

  override initializeNode(n: FileNode, parent?: FileFlatNode): FileFlatNode {
    const node = super.initializeNode(n, parent);
    if (parent === this.userNode && n.id != null && n.id.startsWith('%')) {
      this.filterService.setDisplayName(n.id, n.text);
    }
    if (parent?.node.id != null) {
      let startIdx = parent.node.id.length;
      node.node.text = node.node.text.substr(startIdx);
    }
    return node;
  }
}
