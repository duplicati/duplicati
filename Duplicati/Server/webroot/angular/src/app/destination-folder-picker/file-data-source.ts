import { CollectionViewer, DataSource, SelectionChange } from "@angular/cdk/collections";
import { FlatTreeControl } from "@angular/cdk/tree";
import { Injectable } from "@angular/core";
import { catchError, filter, ReplaySubject } from "rxjs";
import { connect } from "rxjs";
import { Subscription } from "rxjs";
import { concat } from "rxjs";
import { take } from "rxjs";
import { tap } from "rxjs";
import { map } from "rxjs";
import { of } from "rxjs";
import { BehaviorSubject, merge, Observable } from "rxjs";
import { FileNode, FileService } from "../services/file.service";

export class FileFlatNode {
  public entrytype: string = '';
  public invisible = false;

  public subscription?: Subscription;

  get text(): string {
    return this.node.text;
  }

  constructor(
    public node: FileNode,
    public level = 0,
    public expandable = false,
    public isLoading = false
  ) { }
}

export class FileDatabase {
  protected userNode = new FileFlatNode({
    text: 'User data',
    root: true,
    iconCls: 'x-tree-icon-userdata'
  }, 0, true);
  protected systemNode = new FileFlatNode({
    text: 'System data',
    root: true,
    iconCls: 'x-tree-icon-computer'
  }, 0, true);

  rootLevelNodes: FileFlatNode[] = [
    this.userNode, this.systemNode
  ];

  cachedNodes = new Map<string | number, BehaviorSubject<FileFlatNode[] | undefined>>();
  rootCacheLoaded = new BehaviorSubject<boolean>(false);

  constructor(private onlyFolders: boolean, private fileService: FileService) { }

  initialData(hideUserNode: boolean): FileFlatNode[] {
    this.fetchInitialNodes();
    let nodes = this.rootLevelNodes;
    if (hideUserNode) {
      nodes = this.rootLevelNodes.filter(d => d !== this.userNode);
    }
    return nodes;
  }

  public getRootNode(path: string | undefined): FileFlatNode {
    if (path?.startsWith('%')) {
      return this.userNode;
    } else {
      return this.systemNode;
    }
  }

  private fetchInitialNodes(): void {
    this.fileService.getFileChildren('/', this.onlyFolders, true).subscribe(children => {
      let userChildren = [];
      let systemChildren = [];
      for (let c of children) {
        const rootNode = this.getRootNode(c.id)
        if (rootNode === this.userNode) {
          userChildren.push(this.initializeNode(c, rootNode));
        } else if (rootNode === this.systemNode) {
          systemChildren.push(this.initializeNode(c, rootNode));
        }
      }
      this.updateCache(0, userChildren);
      this.updateCache(1, systemChildren);
    }, error => {
      this.cacheError(0, error);
      this.cacheError(1, error);
    });
  }

  private cacheError(key: string | number, err: any): void {
    this.cachedNodes.get(key)?.error(err);
  }

  protected getCache(key: string | number): Observable<FileFlatNode[]> {
    if (!this.cachedNodes.has(key)) {
      this.cachedNodes.set(key, new BehaviorSubject<FileFlatNode[] | undefined>(undefined));
    }
    return this.cachedNodes.get(key)!.pipe(
      filter(v => v != null),
      map(v => v!));
  }

  protected updateCache(key: string | number, n: FileFlatNode[]): void {
    if (this.cachedNodes.has(key)) {
      let s = this.cachedNodes.get(key)!;
      if (s.value !== n) {
        s.next(n);
      }
    } else {
      this.cachedNodes.set(key, new BehaviorSubject<FileFlatNode[] | undefined>(n));
    }
  }

  getChildren(node: FileFlatNode): Observable<FileFlatNode[] | undefined> {
    if (node.node.root) {
      // Use number instad of path as key for cache
      let idx = this.rootLevelNodes.indexOf(node);
      if (idx < 0) {
        return of(undefined);
      } else {
        return this.getCache(idx);
      }
    } else if (node.node.id == null) {
      return of(undefined);
    }

    if (this.cachedNodes.has(node.node.id) && !this.cachedNodes.get(node.node.id)!.hasError) {
      return this.getCache(node.node.id);
    } else {
      let obs = this.getCache(node.node.id);
      let s = this.cachedNodes.get(node.node.id);
      // Ignore completion events for the subject
      this.fileService.getFileChildren(node.node.id, this.onlyFolders, true).subscribe(
        v => s?.next(v.map(fileNode => this.initializeNode(fileNode, node))),
        err => s?.error(err));
      return obs;
    }

  }

  protected initializeNode(fileNode: FileNode, parent?: FileFlatNode): FileFlatNode {
    let n = new FileFlatNode(fileNode, parent ? parent.level + 1 : 0, !fileNode.leaf);
    n.entrytype = this.getEntryType(n.node);
    if (parent && parent.invisible) {
      n.invisible = true;
    }
    return n;
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

  private showHiddenFiles: boolean = false;

  constructor(protected treeControl: FlatTreeControl<FileFlatNode>,
    protected fileService: FileService,
    protected database: FileDatabase,
    private errorHandler?: (err: any) => void) {
    // Subscribe to changes before connect(), because that is called too late
    this.treeControl.expansionModel.changed.subscribe(change => {
      if ((change as SelectionChange<FileFlatNode>).added || (change as SelectionChange<FileFlatNode>).removed) {
        this.handleTreeControl(change as SelectionChange<FileFlatNode>);
      }
    });
  }

  connect(collectionViewer: CollectionViewer): Observable<readonly FileFlatNode[]> {
    return merge(collectionViewer.viewChange, this.dataChange).pipe(
      map(() => this.data.filter(n => !n.invisible)));
  }
  disconnect(collectionViewer: CollectionViewer): void { }

  // Handle expand/collapse behaviors
  private handleTreeControl(change: SelectionChange<FileFlatNode>) {
    if (change.added) {
      change.added.forEach(node => this.toggleNode(node, true));
    }
    if (change.removed) {
      change.removed.slice().reverse().forEach(node => this.toggleNode(node, false));
    }
  }

  getParentNode(node: FileFlatNode): FileFlatNode | null {
    if (node.level < 1) {
      return null;
    }
    const index = this.data.indexOf(node);
    if (index < 0) {
      return null;
    }
    for (let i = index; i >= 0; --i) {
      if (this.data[i].level < node.level) {
        return this.data[i];
      }
    }
    return null;
  }

  // Set children of parent node, replaces existing children
  private setChildren(parent: FileFlatNode, children: FileFlatNode[] | null): void {
    // Check if parent is still in list
    const index = this.data.indexOf(parent);
    if (index < 0) {
      return;
    }

    let count = 0;
    // Remove all nodes from data which are under this one
    for (let i = index + 1; i < this.data.length && this.data[i].level > parent.level; i++, count++) {
    }
    this.data.splice(index + 1, count);

    if (children != null) {
      const nodes = children.map(n => {
        if (!this.showHiddenFiles && n.node.hidden) {
          n.invisible = true;
        }
        return n;
      });
      this.data.splice(index + 1, 0, ...nodes);
      for (let m of nodes) {
        // Add recursive child nodes if expanded (after inserting)
        if (this.treeControl.isExpanded(m)) {
          this.toggleNode(m, true);
        }
      }
    }
    // Notify change
    this.dataChange.next(this.data);
  }

  // Toggle node, remove from display list
  private toggleNode(node: FileFlatNode, expand: boolean) {
    const index = this.data.indexOf(node);
    if (index < 0) {
      return;
    }
    node.isLoading = true;
    node.subscription?.unsubscribe();
    if (expand) {
      node.subscription = this.database.getChildren(node).subscribe(
        children => {
          this.setChildren(node, children != null ? children : null);
          node.isLoading = false;
        },
        err => {
          node.isLoading = false;
          if (this.errorHandler) {
            this.errorHandler(err);
          }
        }
      );
    } else {
      this.setChildren(node, null);
      node.isLoading = false;
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
}
