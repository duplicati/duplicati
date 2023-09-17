import { Subscription } from "rxjs";
import { Observable } from "rxjs";
import { FileDatabase, FileDataSource, FileFlatNode } from "../destination-folder-picker/file-data-source";
import { FileFilterService } from "../services/file-filter.service";
import { FileNode, FileService } from "../services/file.service";

export class SourceFileDatabase extends FileDatabase {

  protected sourceNode = new FileFlatNode({
    text: $localize`Source data`,
    root: true,
    iconCls: 'x-tree-icon-others'
  }, 0, true);

  private sourceSubscription?: Subscription;

  constructor(onlyFolders: boolean, fileService: FileService, private filterService: FileFilterService) {
    super(onlyFolders, fileService);
    this.rootLevelNodes.push(this.sourceNode);
  }

  setSourceNodeChildren(childObservable: Observable<FileFlatNode[] | undefined>): void {
    // Insert entry
    const sourceNodeIdx = this.rootLevelNodes.indexOf(this.sourceNode);
    this.getCache(sourceNodeIdx);
    this.sourceSubscription?.unsubscribe();
    this.sourceSubscription = childObservable.subscribe(this.cachedNodes.get(sourceNodeIdx));
  }

  override initializeNode(n: FileNode, parent?: FileFlatNode): FileFlatNode {
    const node = super.initializeNode(n, parent);
    if (parent === this.userNode && n.id != null && n.id.startsWith('%')) {
      this.filterService.setDisplayName(n.id, n.text);
    }
    return node;
  }
}
