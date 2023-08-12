import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { SystemInfo } from '../system-info/system-info';
import { SystemInfoService } from '../system-info/system-info.service';

export interface FileNode {
  root: boolean;
  id?: string;
  text: string;
  cls?: string;
  iconCls: string;
  check?: boolean;
  leaf?: boolean;
  resolvedpath?: string;
  hidden?: boolean;
  systemFile?: boolean;
  temporary?: boolean;
  symlink?: boolean;
  fileSize?: number;
}

@Injectable({
  providedIn: 'root'
})
export class FileService {

  private systemInfo?: SystemInfo;

  constructor(private client: HttpClient,
    private systemInfoService: SystemInfoService) {
    systemInfoService.getState().subscribe(s => this.systemInfo = s);
  }

  get dirsep(): string {
    return this.systemInfo?.DirectorySeparator || '/';
  }

  comparablePath(path: string): string {
    if (path.startsWith('%') && path.endsWith('%')) {
      path += this.dirsep;
    }
    return this.systemInfo?.CaseSensitiveFilesystem ? path : path.toLowerCase();
  }

  pathsEqual(p1: string, p2: string): boolean {
    return this.comparablePath(p1) === this.comparablePath(p2);
  }

  getEntryTypeFromIconCls(cls: string) {
    // Entry type is used in as the ALT entry,
    // to guide screen reading software for visually
    // impaired users

    var res = 'Folder';

    if (cls == 'x-tree-icon-mydocuments')
      res = 'My Documents';
    else if (cls == 'x-tree-icon-mymusic')
      res = 'My Music';
    else if (cls == 'x-tree-icon-mypictures')
      res = 'My Pictures';
    else if (cls == 'x-tree-icon-desktop')
      res = 'Desktop';
    else if (cls == 'x-tree-icon-home')
      res = 'Home';
    else if (cls == 'x-tree-icon-hypervmachine')
      res = 'Hyper-V Machine';
    else if (cls == 'x-tree-icon-hyperv')
      res = 'Hyper-V Machines';
    else if (cls == 'x-tree-icon-broken')
      res = 'Broken access';
    else if (cls == 'x-tree-icon-locked')
      res = 'Access denied';
    else if (cls == 'x-tree-icon-symlink')
      res = 'Symbolic link';
    else if (cls == 'x-tree-icon-leaf')
      res = 'File';

    return res;
  }

  private setIconCls(n: FileNode): FileNode {
    var cp = this.comparablePath(n.id || '');

    if (cp == this.comparablePath('%MY_DOCUMENTS%'))
      n.iconCls = 'x-tree-icon-mydocuments';
    else if (cp == this.comparablePath('%MY_MUSIC%'))
      n.iconCls = 'x-tree-icon-mymusic';
    else if (cp == this.comparablePath('%MY_PICTURES%'))
      n.iconCls = 'x-tree-icon-mypictures';
    else if (cp == this.comparablePath('%DESKTOP%'))
      n.iconCls = 'x-tree-icon-desktop';
    else if (cp == this.comparablePath('%HOME%'))
      n.iconCls = 'x-tree-icon-home';
    else if (cp.substr(cp.length - 1, 1) != this.dirsep)
      n.iconCls = 'x-tree-icon-leaf';

    return n;
  }

  getFileChildren(path: string, onlyfolders?: boolean, showhidden?: boolean): Observable<FileNode[]> {
    return this.client.get<FileNode[]>('/filesystem', {
      params: {
        'onlyfolders': onlyfolders != null ? onlyfolders : false,
        'showhidden': showhidden != null ? showhidden : false,
        'path': path
      }
    }).pipe(map(nodes => nodes.map(n => this.setIconCls(n))));
  }
}
