import { Inject } from '@angular/core';
import { Input, Output } from '@angular/core';
import { Component, EventEmitter } from '@angular/core';
import { ConvertService } from '../../services/convert.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, CommonBackendData } from '../backend-editor';

@Component({
  selector: 'app-mega',
  templateUrl: './mega.component.html',
  styleUrls: ['./mega.component.less']
})
export class MegaComponent implements BackendEditorComponent {
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
    private parser: ParserService,
    private convert: ConvertService,
    private editUri: EditUriService) { }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    this.editUri.mergeServerAndPath(data);
  }
  buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {};
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    // Slightly better error message
    // folder = commonData.path

    const valid = this.validate();
    if (!valid) {
      return undefined;
    }

    return this.convert.format('{0}://{1}{2}',
      this.key,
      this.commonData.path || '',
      this.parser.encodeDictAsUrl(opts)
    );
  }
  extraConnectionTests(): boolean {
    return true;
  }

  private validate(): boolean {
    return this.editUri.requireField(this.commonData, 'username', 'Username')
      && this.editUri.requireField(this.commonData, 'password', 'Password');
  }
}
