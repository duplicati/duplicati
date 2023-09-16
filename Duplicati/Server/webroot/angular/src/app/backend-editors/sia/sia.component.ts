import { Component, EventEmitter, Inject, InjectionToken, Input, Output } from '@angular/core';
import { ConvertService } from '../../services/convert.service';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, CommonBackendData } from '../../backend-editor';

export const EXAMPLE_SIA_SERVER = new InjectionToken<string>("Example sia server", {
  providedIn: 'root', factory: () => '127.0.0.1:9980'
});
export const EXAMPLE_SIA_REDUNDANCY = new InjectionToken<string>("Example sia redundancy", {
  providedIn: 'root', factory: () => '1.5'
});


@Component({
  selector: 'app-editor-sia',
  templateUrl: './sia.component.html',
  styleUrls: ['./sia.component.less']
})
export class SiaComponent implements BackendEditorComponent {
  @Input({ required: true }) commonData!: CommonBackendData;
  @Output() commonDataChange = new EventEmitter<CommonBackendData>();

  get server() {
    return this.commonData.server || '';
  }
  set server(v) {
    this.commonData.server = v;
    this.commonDataChange.emit(this.commonData);
  }

  siaTargetpath: string = '';
  siaPassword: string = '';
  siaRedundancy: string = '';

  constructor(@Inject(BACKEND_KEY) private key: string,
    @Inject(EXAMPLE_SIA_SERVER) public exampleServer: string,
    @Inject(EXAMPLE_SIA_REDUNDANCY) public exampleRedundancy: string,
    private parser: ParserService,
    private convert: ConvertService,
    private dialog: DialogService,
    private editUri: EditUriService) { }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    this.siaTargetpath = parts.get('--sia-targetpath') || this.siaTargetpath;
    this.siaRedundancy = parts.get('--sia-redundancy') || this.siaRedundancy;
    this.siaPassword = parts.get('--sia-password') || this.siaPassword;

    let nukeopts = ['--sia-targetpath', '--sia-redundancy', '--sia-password'];
    for (let x of nukeopts) {
      parts.delete(x);
    }
  }

  buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {
      'sia-password': this.siaPassword,
      'sia-targetpath': this.siaTargetpath,
      'sia-redundancy': this.siaRedundancy
    };

    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    const valid = this.validate();
    if (!valid) {
      return undefined;
    }

    return this.convert.format('{0}://{1}/{2}{3}',
      this.key,
      this.commonData.server || '',
      this.siaTargetpath || '',
      this.parser.encodeDictAsUrl(opts)
    );
  }

  extraConnectionTests(): boolean {
    return true;
  }

  private validate(): boolean {
    let res = this.editUri.requireField(this.commonData, 'server', 'Server');

    let re = new RegExp('^(([a-zA-Z0-9-])|(\/(?!\/)))*$');
    if (res && !re.test(this.siaTargetpath)) {
      this.dialog.dialog('Error', 'Invalid characters in path');
      res = false;
    }
    if (res && this.siaRedundancy.trim().length == 0 || parseFloat(this.siaRedundancy) < 1.0) {
      this.dialog.dialog('Error', 'Minimum redundancy is 1.0');
      res = false;
    }

    return res;
  }
}
