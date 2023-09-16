import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, CommonBackendData } from '../../backend-editor';
import { EMPTY } from 'rxjs';
import { of } from 'rxjs';
import { Observable } from 'rxjs';

@Component({
  selector: 'app-editor-azure',
  templateUrl: './azure.component.html',
  styleUrls: ['./azure.component.less']
})
export class AzureComponent implements BackendEditorComponent {
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

  constructor(@Inject(BACKEND_KEY) private key: string,
    private editUri: EditUriService,
    private parser: ParserService) { }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    this.editUri.mergeServerAndPath(data);
  }
  buildUri(advancedOptions: string[]): Observable<string> {
    let opts: Record<string, string> = {};
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    // Slightly better error message
    //this.commonData.folder = this.commonData.server;

    const valid = this.editUri.requireField(this.commonData, 'username', 'Account name')
      && this.editUri.requireField(this.commonData, 'password', 'Access key')
      && this.editUri.requireField(this.commonData, 'path', 'Container name');
    if (!valid) {
      return EMPTY;
    }

    return of(`${this.key}://${this.commonData.path}${this.parser.encodeDictAsUrl(opts)}`);
  }
  extraConnectionTests(): Observable<boolean> {
    return of(true);
  }

}
