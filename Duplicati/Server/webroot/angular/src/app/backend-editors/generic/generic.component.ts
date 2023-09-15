import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { ConvertService } from '../../services/convert.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, BACKEND_SUPPORTS_SSL, CommonBackendData } from '../../backend-editor';

@Component({
  selector: 'app-generic',
  templateUrl: './generic.component.html',
  styleUrls: ['./generic.component.less']
})
export class GenericComponent implements BackendEditorComponent {
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
  get port() {
    return this.commonData.port || '';
  }
  set port(v) {
    this.commonData.port = v;
    this.commonDataChange.emit(this.commonData);
  }
  get useSSL() {
    return this.commonData.useSSL || false;
  }
  set useSSL(v) {
    this.commonData.useSSL = v;
    this.commonDataChange.emit(this.commonData);
  }

  constructor(@Inject(BACKEND_KEY) private key: string,
    @Inject(BACKEND_SUPPORTS_SSL) public supportsSSL: boolean,
    private parser: ParserService,
    private convert: ConvertService,
    private editUri: EditUriService) { }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void { }

  buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {};
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    return this.convert.format('{0}{1}://{2}{3}/{4}{5}',
      this.key,
      (this.supportsSSL && this.commonData.useSSL) ? 's' : '',
      this.commonData.server || '',
      (this.commonData.port || '') == '' ? '' : ':' + this.commonData.port,
      this.commonData.path || '',
      this.parser.encodeDictAsUrl(opts)
    );
  }

  extraConnectionTests(): boolean {
    return true;
  }
}
