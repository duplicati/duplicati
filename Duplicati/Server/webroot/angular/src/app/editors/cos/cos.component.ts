import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, CommonBackendData } from '../backend-editor';

@Component({
  selector: 'app-cos',
  templateUrl: './cos.component.html',
  styleUrls: ['./cos.component.less']
})
export class CosComponent implements BackendEditorComponent {
  @Input({ required: true }) commonData!: CommonBackendData;
  @Output() commonDataChange = new EventEmitter<CommonBackendData>();

  cosAppId: string = '';
  cosRegion: string = '';
  cosSecretId: string = '';
  cosSecretKey: string = '';
  cosBucket: string = '';

  get path() {
    return this.commonData.path || '';
  }
  set path(v) {
    this.commonData.path = v;
    this.commonDataChange.emit(this.commonData);
  }

  constructor(@Inject(BACKEND_KEY) private key: string,
    private editUri: EditUriService,
    private parser: ParserService) { }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    this.cosAppId = parts.get('--cos-app-id') ?? '';
    this.cosRegion = parts.get('--cos-region') ?? '';
    this.cosSecretId = parts.get('--cos-secret-id') ?? '';
    this.cosSecretKey = parts.get('--cos-secret-key') ?? '';
    this.cosBucket = parts.get('--cos-bucket') ?? '';

    let nukeopts = ['--cos-app-id', '--cos-region', '--cos-secret-id', '--cos-secret-key', '--cos-bucket'];
    for (let n of nukeopts) {
      parts.delete(n);
    }

    this.editUri.mergeServerAndPath(data);
  }
  buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {
      'cos-app-id': this.cosAppId,
      'cos-region': this.cosRegion,
      'cos-secret-id': this.cosSecretId,
      'cos-secret-key': this.cosSecretKey,
      'cos-bucket': this.cosBucket
    };
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    const valid = this.validate();
    if (!valid) {
      return undefined;
    }

    return `${this.key}://${this.commonData.path || ''}${this.parser.encodeDictAsUrl(opts)}`;
  }
  extraConnectionTests(): boolean {
    return true;
  }

  private validate(): boolean {
    return this.editUri.requireField(this, 'cosAppId', 'cos_app_id')
      && this.editUri.requireField(this, 'cosSecretId', 'cos_secret_id')
      && this.editUri.requireField(this, 'cosSecretKey', 'cos_secret_key')
      && this.editUri.requireField(this, 'cosRegion', 'cos_region')
      && this.editUri.requireField(this, 'cosBucket', 'cos_bucket')
  }
}
