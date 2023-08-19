import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { of } from 'rxjs';
import { take } from 'rxjs';
import { BehaviorSubject, map, Observable, zip } from 'rxjs';
import { catchError, filter, switchAll } from 'rxjs/operators';
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
  tooltip?: string;
}

@Injectable({
  providedIn: 'root'
})
export class FileService {

  private systemInfo?: SystemInfo;
  private systemInfoReady = new BehaviorSubject(false);
  private defunctMap = new Map<string, boolean>();

  constructor(private client: HttpClient,
    private systemInfoService: SystemInfoService) {
    systemInfoService.getState().subscribe(s => {
      this.systemInfo = s; this.systemInfoReady.next(true);
    });
  }

  get dirsep(): string {
    return this.systemInfo?.DirectorySeparator || '/';
  }
  get isCaseSensitive(): boolean {
    return this.systemInfo?.CaseSensitiveFilesystem || false;
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

  setIconCls(n: FileNode): FileNode {
    const id = n.id || '';
    const cp = this.comparablePath(id);

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
    else if (id.startsWith('%HYPERV%\\') && id.length >= 10) {
      n.iconCls = 'x-tree-icon-hypervmachine';
      n.tooltip = `ID: ${id.substring(9, id.length)}`;
    } else if (id.startsWith('%HYPERV%'))
      n.iconCls = 'x-tree-icon-hyperv';
    else if (id.startsWith('%MSSQL%\\') && id.length >= 9) {
      n.iconCls = 'x-tree-icon-mssqldb';
      n.tooltip = `ID: ${id.substring(8, id.length)}`;
    } else if (id.startsWith('%MSSQL%'))
      n.iconCls = 'x-tree-icon-mssql';
    else if (this.defunctMap.get(cp))
      n.iconCls = 'x-tree-icon-broken';
    else if (!cp.endsWith(this.dirsep))
      n.iconCls = 'x-tree-icon-leaf';

    return n;
  }

  getFileChildren(path: string, onlyfolders?: boolean, showhidden?: boolean): Observable<FileNode[]> {

    const req = this.client.get<FileNode[]>('/filesystem', {
      params: {
        'onlyfolders': onlyfolders != null ? onlyfolders : false,
        'showhidden': showhidden != null ? showhidden : false,
        'path': path
      }
    });
    // Wait for system info to be ready before assigning icons
    return zip(req, this.systemInfoReady.pipe(filter(v => v)), (n, v) => n).pipe(
      map(nodes => nodes.map(n => this.setIconCls(n))));
  }

  whenDirsepReady(): Observable<string> {
    return this.systemInfoReady.pipe(
      filter(v => v),
      take(1),
      map(() => this.dirsep));
  }

  checkDefunct(n: FileNode): Observable<boolean> {
    if (n.id == null) {
      return of(false);
    }
    const cp = this.comparablePath(n.id);
    if (!this.defunctMap.has(cp) && n.iconCls != 'x-tree-icon-hyperv' && n.iconCls != 'x-tree-icon-hypervmachine'
      && n.iconCls != 'x-tree-icon-mssql' && n.iconCls != 'x-tree-icon-mssqldb') {
      this.defunctMap.set(cp, true);

      let p = n.id;
      return this.whenDirsepReady().pipe(
        map(() => {
          if (p.startsWith('%') && p.endsWith('%')) {
            p += this.dirsep;
          }
          return this.client.post('/filesystem/validate', '', { params: { path: p } }).pipe(
            map(() => {
              this.defunctMap.set(cp, false);
              return false;
            }),
            catchError(err => {
              return of(true);
            }));
        }),
        switchAll());

    } else {
      return of(false);
    }
  }
}
