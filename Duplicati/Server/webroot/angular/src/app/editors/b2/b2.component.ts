import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, CommonBackendData } from '../backend-editor';

@Component({
  selector: 'app-b2',
  templateUrl: './b2.component.html',
  styleUrls: ['./b2.component.less']
})
export class B2Component implements BackendEditorComponent {
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
    if (parts.get('--b2-accountid')) {
      data.username = parts.get('--b2-accountid');
    }
    parts.delete('--b2-accountid');
    if (parts.get('--b2-applicationkey')) {
      data.password = parts.get('--b2-applicationkey');
    }
    parts.delete('--b2-applicationkey');
  }

  buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {};
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    // Slightly better error message
    //this.commonData.folder = this.commonData.server;

    const valid = this.validate();
    if (!valid) {
      return undefined;
    }

    return `${this.key}://${this.commonData.server || ''}/${this.commonData.path || ''}${this.parser.encodeDictAsUrl(opts)}`;
  }

  private validate(): boolean {
    let res = this.editUri.requireField(this.commonData, 'server', 'Bucket Name')
      && this.editUri.requireField(this.commonData, 'username', 'B2 Cloud Storage Account ID')
      && this.editUri.requireField(this.commonData, 'password', 'B2 Cloud Storage Application Key');

    if (res) {
      let bucketname = this.commonData.server || '';
      let ix = bucketname.search(/[^A-Za-z0-9-]/g);

      if (ix >= 0) {
        this.dialog.dialog('Error', `The 'Bucket Name' contains an invalid character: ${bucketname[ix]} (value: ${bucketname.charCodeAt(ix)}, index: ${ix})`);
        res = false;
      }
    }
    if (res) {
      let pathname = this.commonData.path || '';
      for (var i = pathname.length - 1; i >= 0; i--) {
        var char = pathname.charCodeAt(i);

        if (char == '\\'.charCodeAt(0) || char == 127 || char < 32) {
          this.dialog.dialog('Error', `The 'Path' field contains an invalid character: ${pathname[i]} (value: ${char}, index: ${i})`);
          res = false;
          break;
        }
      }
    }

    return res;
  }
  extraConnectionTests(): boolean {
    return true;
  }
}
