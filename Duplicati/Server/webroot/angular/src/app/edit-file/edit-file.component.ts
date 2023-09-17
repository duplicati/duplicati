import { Component, EventEmitter, Input, Output, SimpleChanges } from '@angular/core';
import { DialogService } from '../services/dialog.service';
import { EditUriService } from '../services/edit-uri.service';
import { ParserService } from '../services/parser.service';
import { BackendEditorComponent, CommonBackendData } from '../backend-editor';
import { EMPTY, Observable, of } from 'rxjs';

@Component({
  templateUrl: './edit-file.component.html',
  styleUrls: ['./edit-file.component.less']
})
export class EditFileComponent implements BackendEditorComponent {
  @Input({ required: true }) commonData!: CommonBackendData;
  @Output() commonDataChange = new EventEmitter<CommonBackendData>();

  get path() {
    return this.commonData.path || '';
  }
  set path(v) {
    this.commonData.path = v;
    this.commonDataChange.emit(this.commonData);
  }
  get username() {
    return this.commonData.username;
  }
  set username(v) {
    this.commonData.username = v;
    this.commonDataChange.emit(this.commonData);
  }
  get password() {
    return this.commonData.password;
  }
  set password(v) {
    this.commonData.password = v;
    this.commonDataChange.emit(this.commonData);
  }

  hideFolderBrowser: boolean = false;
  showHiddenFolders: boolean = false;

  constructor(private editUri: EditUriService,
    private dialog: DialogService,
    private parser: ParserService) { }

  ngOnChanges(changes: SimpleChanges) {
    if ('commonData' in changes) {
      this.hideFolderBrowser = this.path != '';
    }
  }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    // File URI is complicated, because drive letter may be in server component
    let path = data.path || '';
    let server = data.server || '';
    let uri = parts.get('source_uri') || '';
    if (path.length === 0 && server.length === 0) {
      let queryIdx = uri.indexOf('?');
      if (queryIdx < 0) {
        queryIdx = uri.length;
      }
      this.commonData.path = uri.substring(0, queryIdx);
    } else if (server.length === 1) {
      // Drive letter
      let queryIdx = uri.indexOf('?');
      if (queryIdx < 0) {
        queryIdx = uri.length;
      }
      data.path = uri.substring(uri.indexOf('://') + 3, queryIdx);
    } else {
      let dirsep = '/';
      let newPath = server;
      if (path.indexOf('\\') >= 0 || server.indexOf('\\') >= 0) {
        dirsep = '\\';
      }
      if (!server.endsWith(dirsep)) {
        newPath += dirsep;
      }
      if (path.length > 0) {
        newPath += path;
      }
      data.path = newPath;
    }
  }
  buildUri(advancedOptions: string[]): Observable<string> {
    let opts: Record<string, string> = {};
    if (!this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts)) {
      this.dialog.dialog($localize`Error`, $localize`Failed to process advanced options`);
      return EMPTY;
    }
    return of(`file://${this.path}${this.parser.encodeDictAsUrl(opts)}`);
  }
  extraConnectionTests(): Observable<boolean> {
    // Nothing to test
    return of(true);
  }
}
