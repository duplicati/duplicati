import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, CommonBackendData } from '../../backend-editor';
import { Observable, of } from 'rxjs';
import { EMPTY } from 'rxjs';

@Component({
  selector: 'app-editor-e2',
  templateUrl: './e2.component.html',
  styleUrls: ['./e2.component.less']
})
export class E2Component implements BackendEditorComponent {
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
    return this.commonData.username || '';
  }
  set username(v) {
    this.commonData.username = v;
    this.commonDataChange.emit(this.commonData);
  }
  get password() {
    return this.commonData.password || '';
  }
  set password(v) {
    this.commonData.password = v;
    this.commonDataChange.emit(this.commonData);
  }
  get server() {
    return this.commonData.server || '';
  }
  set server(v) {
    this.commonData.server = v;
    this.commonDataChange.emit(this.commonData);
  }


  constructor(@Inject(BACKEND_KEY) private key: string,
    private editUri: EditUriService,
    private dialog: DialogService,
    private parser: ParserService) { }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    if (parts.get('--access-key-id')) {
      data.username = parts.get('--access-key-id');
    }
    parts.delete('--access-key-id');
    if (parts.get('--access-key-secret')) {
      data.password = parts.get('--access-key-secret');
    }
    parts.delete('--access-key-secret');
  }

  buildUri(advancedOptions: string[]): Observable<string> {
    let opts: Record<string, string> = {};
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    // Slightly better error message
    //this.commonData.folder = this.commonData.server;

    const valid = this.validate();
    if (!valid) {
      return EMPTY;
    }

    return of(`${this.key}://${this.commonData.server || ''}/${this.commonData.path || ''}${this.parser.encodeDictAsUrl(opts)}`);
  }

  private validate(): boolean {
    let res = this.editUri.requireField(this.commonData, 'username', $localize`Idrivee2 Access Key Id`)
      && this.editUri.requireField(this.commonData, 'password', $localize`Idrivee2 Access Key Secret`)
      && this.editUri.requireField(this.commonData, 'server', $localize`Bucket name`);

    if (res) {
      let bucketname = this.commonData.server || '';
      let ix = bucketname.search(/[^A-Za-z0-9-]/g);

      if (ix >= 0) {
        this.dialog.dialog($localize`Error`, $localize`The 'Bucket Name' contains an invalid character: ${bucketname[ix]} (value: ${bucketname.charCodeAt(ix)}, index: ${ix})`);
        res = false;
      }
    }
    if (res) {
      let pathname = this.commonData.path || '';
      for (var i = pathname.length - 1; i >= 0; i--) {
        var char = pathname.charCodeAt(i);

        if (char == '\\'.charCodeAt(0) || char == 127 || char < 32) {
          this.dialog.dialog($localize`Error`, $localize`The 'Path' field contains an invalid character: ${pathname[i]} (value: ${char}, index: ${i})`);
          res = false;
          break;
        }
      }
    }

    return res;
  }
  extraConnectionTests(): Observable<boolean> {
    return of(true);
  }
}
