import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { tap } from 'rxjs';
import { map } from 'rxjs';
import { of } from 'rxjs';
import { Observable } from 'rxjs';
import { ConvertService } from './convert.service';
import { FileNode, FileService } from './file.service';

export class FilterClass {

  get name() { return this.vals.name; }
  get key() { return this.vals.key; }
  get prefix() { return this.vals.prefix || ''; }
  get suffix() { return this.vals.suffix || ''; }
  get stripSep() { return this.vals.stripSep; }
  get exclude() { return this.vals.exclude; }
  get rx() { return this.vals.rx; }

  constructor(private vals: {
    name: string,
    key: string,
    prefix?: string,
    suffix?: string,
    stripSep?: boolean,
    exclude?: string[],
    rx?: string
  }) { }

  match(txt: string, replaceDirsep: (s: string) => string): { type: string, body: string } | undefined {
    const pre = replaceDirsep(this.prefix);
    const suf = replaceDirsep(this.suffix);

    if (!txt.startsWith(pre) || !txt.endsWith(suf)) {
      return undefined;
    }

    const type = this.key;
    let body = txt.substring(pre.length, txt.length - suf.length);
    if (body.length >= 2 && body[1] === '[') {
      // Strip brackets
      body = body.substr(1, body.length - 1);
    }

    if (this.exclude != null) {
      for (const ex of this.exclude) {
        if (body.indexOf(replaceDirsep(ex)) >= 0) {
          return undefined;
        }
      }
    }
    return { type: type, body: body };
  }

  build(body: string, dirsep: string, replaceDirsep: (s: string) => string): string {
    const pre = replaceDirsep(this.prefix);
    const suf = replaceDirsep(this.suffix);

    if (pre.length >= 2 && pre[1] == '[') {
      // Regexp encode body...
    }
    if (this.stripSep == true && body.endsWith(dirsep)) {
      // Remove trailing dirsep
      body = body.slice(0, -1);
    }
    return pre + body + suf;
  }
}

interface FilterGroups {
  FilterGroups: Record<string, string[]>;
}

@Injectable({
  providedIn: 'root'
})
export class FileFilterService {

  private get dirsep(): string {
    return this.fileService.dirsep;
  }

  // TODO: Inject this
  private filterClasses: FilterClass[] = [new FilterClass({
    name: $localize`Exclude directories whose names contain`,
    key: '-dir*',
    prefix: '-*',
    suffix: '*!'
  }), new FilterClass({
    name: $localize`Exclude files whose names contain`,
    key: '-file*',
    prefix: '-[.*',
    suffix: '[^\\!]*]',    // Escape dirsep inside regexp. Required on Windows, no effect on other platforms.
    rx: '\\-\\[\\.\\*[\\^\\!\\]\\*\\]'
  }), new FilterClass({
    name: $localize`Exclude folder`,
    key: '-folder',
    prefix: '-',
    suffix: '!',
    stripSep: true,
    rx: '\\-(.*)\\!'
  }), new FilterClass({
    name: $localize`Exclude file`,
    key: '-path',
    prefix: '-',
    stripSep: true,
    exclude: ['*', '?', '{'],
    rx: '\\-([^\\[\\{\\*\\?]+)'
  }), new FilterClass({
    name: $localize`Exclude file extension`,
    key: '-ext',
    rx: '\\-\\*\.(.*)',
    prefix: '-*.'
  }), new FilterClass({
    name: $localize`Exclude regular expression`,
    key: '-[]',
    prefix: '-[',
    suffix: ']'
  }), new FilterClass({
    name: $localize`Include regular expression`,
    key: '+[]',
    prefix: '+[',
    suffix: ']'
  }), new FilterClass({
    name: $localize`Exclude filter group`,
    key: '-{}',
    prefix: '-{',
    suffix: '}'
  }),
  //// Since all the current filter groups are intended for exclusion, there isn't a reason to show the 'Include group' section in the UI yet.
  //new FilterClass( {
  //  name: $localize`Include filter group`,
  //  key: '+{}',
  //  prefix: '+{',
  //  suffix: '}'
  //}),
  new FilterClass({
    name: $localize`Include expression`,
    key: '+',
    prefix: '+'
  }), new FilterClass({
    name: $localize`Exclude expression`,
    key: '-',
    prefix: '-'
  })];
  private filterGroups: ({ name: string, value: string })[] = [{
    name: $localize`Default excludes`,
    value: 'DefaultExcludes'
  }, {
    //// As the DefaultIncludes are currently empty, we don't need to include them in the UI yet.
    //    name: $localize`Default includes`,
    //    value: 'DefaultIncludes'
    //}, {
    name: $localize`System Files`,
    value: 'SystemFiles'
  }, {
    name: $localize`Operating System`,
    value: 'OperatingSystem'
  }, {
    name: $localize`Cache Files`,
    value: 'CacheFiles'
  }, {
    name: $localize`Temporary Files`,
    value: 'TemporaryFiles'
  }, {
    name: $localize`Applications`,
    value: 'Applications'
  }];
  private fileAttributes: ({ name: string, value: string })[] = [
    { name: $localize`Hidden files`, value: 'hidden' },
    { name: $localize`System files`, value: 'system' },
    { name: $localize`Temporary files`, value: 'temporary' },
  ];

  private filterTypeMap: Map<string, FilterClass>;
  private filterGroupMap?: Map<string, string[]>;
  private displayMap = new Map<string, string>();

  constructor(private convertService: ConvertService,
    private client: HttpClient,
    private fileService: FileService) {
    this.filterTypeMap = new Map<string, FilterClass>(this.filterClasses.map(f => [f.key, f]));
  }

  getFilterClasses(): FilterClass[] {
    return this.filterClasses;
  }

  loadFilterGroups(reload?: boolean): Observable<Map<string, string[]>> {
    if (reload || this.filterGroupMap == null) {
      return this.client.get<FilterGroups>('/systeminfo/filtergroups').pipe(
        map(groups => {
          this.filterGroupMap = new Map<string, string[]>(Object.entries(groups.FilterGroups));
          return this.filterGroupMap;
        })
      );
    }
    return of(this.filterGroupMap);
  }

  getFilterGroups(): ({ name: string, value: string })[] {
    return this.filterGroups;
  }

  getFileAttributes(): ({ name: string, value: string })[] {
    return this.fileAttributes;
  }

  splitFilterIntoTypeAndBody(filter: string | undefined): { type: string, body: string } | undefined {
    if (filter == null) {
      return undefined;
    }
    if (this.dirsep == null) {
      throw new Error('Missing dirsep');
    }

    const replaceDirsep = (s: string) => this.convertService.replaceAll(s, '!', this.dirsep!);
    let matches: { type: string, body: string }[] = [];
    this.filterClasses.forEach(filterClass => {
      const match = filterClass.match(filter, replaceDirsep);
      if (match != null) {
        matches.push(match);
      }
    });

    if (matches.length == 0) {
      return undefined;
    }

    // Select match with shortest body, meaning longest prefix and suffix
    let shortestIdx = 0;
    let shortestLen = filter.length;
    for (let i = 0; i < matches.length; i++) {
      if (matches[i].body.length < shortestLen) {
        shortestIdx = i;
        shortestLen = matches[i].body.length;
      }
    }
    return matches[shortestIdx];
  }

  buildFilter(type: string | undefined, body: string): string {
    if (this.dirsep == null) {
      throw new Error('Missing dirsep');
    }

    if (type == null || !this.filterTypeMap.has(type)) {
      return body;
    }
    const replaceDirsep = (s: string) => this.convertService.replaceAll(s, '!', this.dirsep!);
    const f = this.filterTypeMap.get(type)!;
    return f.build(body, this.dirsep!, replaceDirsep);
  }

  filterToRegexpStr(filter: string): string {
    const firstChar = filter.substr(0, 1);
    const lastChar = filter.substr(filter.length - 1, 1);
    const isFilterGroup = firstChar === '{' && lastChar === '}';
    if (isFilterGroup && this.filterGroupMap != null) {
      // Replace filter groups with filter strings
      let filterGroups = filter.substr(1, filter.length - 2).split(',');
      let filterStrings: string[] = [];
      for (const group of filterGroups) {
        filterStrings = filterStrings.concat(this.filterGroupMap.get(group.trim()) || []);
      }

      filter = filterStrings.map(s => `(?:${this.filterToRegexpStr(s)})`).join('|');
    } else {
      let rx = firstChar === '[' && lastChar === ']';
      if (rx) {
        filter = filter.substr(1, filter.length - 2);
      } else {
        filter = this.convertService.globToRegexp(filter);
      }
    }
    return filter;
  }

  filterListToRegexps(filters: string[], caseSensitive: boolean): ([boolean, RegExp])[] {
    let res: ([boolean, RegExp])[] = [];
    for (const f of filters) {
      if (f == null || f.length == 0) {
        continue;
      }

      const flag = f.substr(0, 1);
      const filter = f.substr(1);

      try {
        let regexp = this.filterToRegexpStr(filter);
        res.push([flag === '+', new RegExp(regexp, caseSensitive ? 'g' : 'gi')]);
      } catch (e) { }
    }

    return res;
  }

  buildExcludeMap(filters: string[]): { excludemap: Map<string, boolean>, filterList?: ([boolean, RegExp])[] } {
    let excludemap = new Map<string, boolean>();
    let filterList: ([boolean, RegExp])[] | undefined;
    let anySpecials = false;
    for (let f of filters) {
      const res = this.splitFilterIntoTypeAndBody(f);
      if (res != null) {
        const { type, body } = res;
        if (type.startsWith('+') || body.includes('?') || body.includes('*')) {
          anySpecials = true;
        } else if (type === '-path') {
          excludemap.set(this.fileService.comparablePath(body), true);
        } else if (type === '-folder') {
          excludemap.set(this.fileService.comparablePath(body + this.dirsep), true);
        } else {
          anySpecials = true;
        }
      }
    }

    if (anySpecials) {
      filterList = this.filterListToRegexps(filters, this.fileService.isCaseSensitive);
    }

    return { excludemap, filterList };
  }

  buildPartialIncludeMap(sources: string[]): Map<string, boolean> {
    let map = new Map<string, boolean>();
    for (const s of sources) {
      const cp = this.fileService.comparablePath(s);
      let parts = cp.split(this.dirsep);
      if (parts.length == 0) {
        continue;
      }
      // Remove last part component if cp ends with dirsep
      if (parts[parts.length - 1].length == 0) {
        parts.pop();
      }
      // Only go through parts of parent directories
      if (parts.length > 0) {
        parts.pop();
      }
      let partialPath: string[] = [];
      for (let p of parts) {
        partialPath.push(p);
        let r = partialPath.join(this.dirsep);
        if (!r.endsWith(this.dirsep)) {
          r += this.dirsep;
        }
        map.set(r, true);
      }
    }
    return map;
  }

  setDisplayName(path: string, name: string): void {
    const cp = this.fileService.comparablePath(path);
    this.displayMap.set(cp, name);
  }

  getSourceDisplayName(s: string): string {
    let txt = s;
    const k = this.fileService.comparablePath(s);
    if (k.startsWith('%')) {
      const nx = k.substr(1).indexOf('%') + 2;
      if (nx > 1) {
        let key = this.fileService.comparablePath(k.substr(0, nx));
        txt = this.displayMap.get(k) || (this.displayMap.get(key) || key) + txt.substr(nx);
      }
    }
    return txt;
  }

  isExcludedBySize(n: FileNode, excludeSize: number | null): boolean {
    if (excludeSize != null && n.fileSize != null) {
      return n.fileSize > excludeSize;
    }
    return false;
  }

  isExcludedByAttributes(n: FileNode, excludeAttributes: string[] | null): boolean {
    if (excludeAttributes != null && excludeAttributes.length > 0) {
      return (excludeAttributes.includes('hidden') && n.hidden)
        || (excludeAttributes.includes('system') && n.systemFile)
        || (excludeAttributes.includes('temporary') && n.temporary)
        || false;
    }
    return false;
  }

  evalFilter(path: string, filters: [boolean, RegExp][], defaultValue?: boolean): boolean;
  evalFilter(path: string, filters: [boolean, RegExp][], defaultValue: null): boolean | null;
  evalFilter(path: string, filters: [boolean, RegExp][], defaultValue?: boolean | null): boolean | null {
    for (const f of filters) {
      const m = path.match(f[1]);
      // Regex such as .* might match empty string at the end which is unwanted
      // Check that the first match covers the full string
      if (m && m.length >= 1 && m[0].length == path.length) {
        return f[0];
      }
    }
    return defaultValue === undefined ? true : defaultValue;
  }

  insertSourceDirectory(newPath: string, sources: string[]): void {
    const cp = this.fileService.comparablePath(newPath);
    const isDir = cp.endsWith(this.fileService.dirsep);
    if (isDir) {
      for (let i = sources.length - 1; i >= 0; --i) {
        const s = this.fileService.comparablePath(sources[i]);
        if (s === cp) {
          // Path already in list
          return;
        }
        if (s.endsWith(this.dirsep) && cp.startsWith(s)) {
          // TODO: Check if it should be added due to attributes
          // Parent already in list
          return;
        } else if (s.startsWith(cp)) {
          // TODO: Check if it can be removed due to attributes
          // Child in list, remove
          sources.splice(i, 1);
        }
      }
    }
    sources.push(newPath);
  }

  removePathFromArray(array: string[], path: string): boolean {
    const i = array.findIndex(p => this.fileService.pathsEqual(p, path));
    if (i >= 0) {
      array.splice(i, 1);
      return true;
    }
    return false;
  }


  // Remove exclude filters that apply directly to path or children of path (if it is a directory)
  // Only applies to path or folder filter types
  removeExcludeFiltersOfChildren(filters: string[], path: string): boolean {
    const cp = this.fileService.comparablePath(path);
    const isDir = cp.endsWith(this.dirsep);
    let changed = false;
    for (let i = filters.length - 1; i >= 0; i--) {
      let n = this.splitFilterIntoTypeAndBody(filters[i]);
      if (n != null) {
        if (isDir) {
          if (n.type === '-path' || n.type === '-folder') {
            const filterPath = this.fileService.comparablePath(n.body + (n.type === '-folder' ? this.dirsep : ''));
            if (filterPath.startsWith(cp)) {
              filters.splice(i, 1);
              changed = true;
            }
          }
        } else {
          if (n.type === '-path' && this.fileService.comparablePath(n.body) === cp) {
            filters.splice(i, 1);
            changed = true;
          }
        }
      }
    }
    return changed;
  }
}
