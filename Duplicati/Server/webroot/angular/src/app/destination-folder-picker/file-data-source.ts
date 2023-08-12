import { CollectionViewer, DataSource, SelectionChange } from "@angular/cdk/collections";
import { FlatTreeControl } from "@angular/cdk/tree";
import { Injectable } from "@angular/core";
import { filter } from "rxjs";
import { take } from "rxjs";
import { tap } from "rxjs";
import { map } from "rxjs";
import { of } from "rxjs";
import { BehaviorSubject, merge, Observable } from "rxjs";
import { FileNode, FileService } from "../services/file.service";

export class FileFlatNode {
  public selected = false;
  public entrytype: string = '';
  public invisible = false;

  get text(): string {
    return this.node.text;
  }

  constructor(
    public node: FileNode,
    public level = 1,
    public expandable = false,
    public isLoading = false
  ) { }
}

@Injectable({ providedIn: 'root' })
export class FolderDatabase {
  rootLevelNodes: FileNode[] = [
    {
      text: 'User data',
      root: true,
      iconCls: 'x-tree-icon-userdata'
    }, {
      text: 'System data',
      root: true,
      iconCls: 'x-tree-icon-computer'
    }
  ];


  cachedNodes = new Map<string | number, FileNode[]>();
  rootCacheLoaded = new BehaviorSubject<boolean>(false);

  constructor(private fileService: FileService) { }

  initialData(hideUserNode: boolean): FileFlatNode[] {
    this.fetchInitialNodes();
    if (hideUserNode) {
      return [new FileFlatNode(this.rootLevelNodes[1], 0, true)];
    } else {
      return this.rootLevelNodes.map(n => new FileFlatNode(n, 0, true));
    }
  }

  private fetchInitialNodes(): void {
    this.fileService.getFileChildren('/', true, true).subscribe(children => {
      let userChildren = [];
      let systemChildren = [];
      for (let c of children) {
        if (c.id?.indexOf('%') === 0) {
          userChildren.push(c);
        } else {
          systemChildren.push(c);
        }
      }
      this.cachedNodes.set(0, userChildren);
      this.cachedNodes.set(1, systemChildren);
      this.rootCacheLoaded.next(true);
    }
    );
  }


  getChildren(node: FileNode): Observable<FileNode[] | undefined> {
    if (node.root) {
      // Use number instad of path as key for cache
      let idx = this.rootLevelNodes.indexOf(node);
      if (idx < 0) {
        return of(undefined);
      } else {
        return this.rootCacheLoaded.pipe(
          filter(loaded => loaded),
          map(() => this.cachedNodes.get(idx)),
          take(1));
      }
    } else if (node.id == null) {
      return of(undefined);
    }
    if (this.cachedNodes.has(node.id)) {
      return of(this.cachedNodes.get(node.id));
    } else {
      return this.fileService.getFileChildren(node.id, true, true).pipe(
        tap(children => {
          this.cachedNodes.set(node.id!, children);
        })
      );
    }

  }

  getEntryType(node: FileNode): string {
    return this.fileService.getEntryTypeFromIconCls(node.iconCls);
  }
  isExpandable(node: FileNode): boolean {
    return !node.leaf;
  }
}

export class FileDataSource implements DataSource<FileFlatNode> {
  dataChange = new BehaviorSubject<FileFlatNode[]>([]);

  get data(): FileFlatNode[] {
    return this.dataChange.value;
  }

  set data(value: FileFlatNode[]) {
    this.treeControl.dataNodes = value;
    this.dataChange.next(value);
  }

  private selectedNode?: FileFlatNode;
  private selectedPath?: string;

  private showHiddenFiles: boolean = false;

  constructor(private treeControl: FlatTreeControl<FileFlatNode>,
    private fileService: FileService,
    private database: FolderDatabase) { }

  connect(collectionViewer: CollectionViewer): Observable<readonly FileFlatNode[]> {
    this.treeControl.expansionModel.changed.subscribe(change => {
      if ((change as SelectionChange<FileFlatNode>).added || (change as SelectionChange<FileFlatNode>).removed) {
        this.handleTreeControl(change as SelectionChange<FileFlatNode>);
      }
    });
    return merge(collectionViewer.viewChange, this.dataChange).pipe(map(() => this.data.filter(
      n => !n.invisible
    )));
  }
  disconnect(collectionViewer: CollectionViewer): void { }

  // Handle expand/collapse behaviors
  handleTreeControl(change: SelectionChange<FileFlatNode>) {
    if (change.added) {
      change.added.forEach(node => this.toggleNode(node, true));
    }
    if (change.removed) {
      change.removed.slice().reverse().forEach(node => this.toggleNode(node, false));
    }
  }

  // Toggle node, remove from display list
  toggleNode(node: FileFlatNode, expand: boolean) {
    const index = this.data.indexOf(node);
    if (index < 0) {
      return;
    }
    node.isLoading = true;
    if (expand) {
      this.database.getChildren(node.node).subscribe(
        children => {
          if (children) {
            const nodes = children.map(n => {
              let flatNode = new FileFlatNode(n, node.level + 1, !n.leaf);
              this.initializeNode(flatNode, node);
              return flatNode;
            });
            this.data.splice(index + 1, 0, ...nodes);
            this.dataChange.next(this.data);
          }
          node.isLoading = false;
        }
      );
    } else {
      let count = 0;
      // Remove all nodes from data which are under this one
      for (let i = index + 1; i < this.data.length && this.data[i].level > node.level; i++, count++) { }
      this.data.splice(index + 1, count);
      // Notify change
      this.dataChange.next(this.data);
      node.isLoading = false;
    }
  }

  setSelected(node: FileFlatNode | string | undefined): void {
    if (this.selectedNode != null) {
      this.selectedNode.selected = false;
      this.selectedNode = undefined;
    }
    let path: string | undefined = undefined;
    if (typeof node === 'string') {
      path = node;
      node = this.data.find(n => n.node.id != null
        && this.fileService.pathsEqual(n.node.id, path!));
    } else if (node instanceof FileFlatNode) {
      path = node.node.id;
    }
    this.selectedPath = path;
    if (node != null) {
      this.selectedNode = node;
      this.selectedNode.selected = true;
    }
  }

  showHidden(show: boolean): void {
    this.showHiddenFiles = show;
    // Apply visibility
    for (let i = 0; i < this.data.length;) {
      const node = this.data[i];
      if (node.invisible && this.showHiddenFiles && node.node.hidden === true) {
        node.invisible = false;
        // Apply to children
        for (i = i + 1; i < this.data.length && this.data[i].level > node.level; ++i) {
          this.data[i].invisible = false;
        }
      } else if (!node.invisible && !this.showHiddenFiles && node.node.hidden === true) {
        node.invisible = true;
        // Apply to children
        for (i = i + 1; i < this.data.length && this.data[i].level > node.level; ++i) {
          this.data[i].invisible = true;
        }
      } else {
        // For loops will increment i otherwise
        ++i;
      }
    }
    this.dataChange.next(this.data);
  }

  initializeNode(n: FileFlatNode, parent?: FileFlatNode): void {
    n.entrytype = this.database.getEntryType(n.node);
    if (this.selectedPath != null) {
      if (n.node.id != null
        && this.fileService.pathsEqual(n.node.id, this.selectedPath)) {
        n.selected = true;
        this.selectedNode = n;
      }
    }
    if (!this.showHiddenFiles && n.node.hidden) {
      n.invisible = true;
    }
    if (parent && parent.invisible) {
      n.invisible = true;
    }
  }
}
