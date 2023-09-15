import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { StorjService } from '../services/storj.service';
import { ConvertService } from '../../services/convert.service';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, CommonBackendData } from '../../backend-editor';

@Component({
  selector: 'app-storj',
  templateUrl: './storj.component.html',
  styleUrls: ['./storj.component.less']
})
export class StorjComponent implements BackendEditorComponent {
  @Input({ required: true }) commonData!: CommonBackendData;
  @Output() commonDataChange = new EventEmitter<CommonBackendData>();

  storjAuthMethod: string = 'API key';
  storjSatellite: string = '';
  storjSatelliteCustom: string = '';
  storjApiKey: string = '';
  storjSecret: string = '';
  storjSecretVerify: string = '';
  storjSharedAccess: string = '';
  storjBucket: string = '';
  storjFolder: string = '';

  storjSatellites: Record<string, string | null> = {};
  storjAuthMethods: Record<string, string | null> = {};

  constructor(@Inject(BACKEND_KEY) protected key: string,
    protected parser: ParserService,
    protected convert: ConvertService,
    protected dialog: DialogService,
    protected storjService: StorjService,
    protected editUri: EditUriService) { }

  ngOnInit() {
    this.storjSatellite = this.storjService.defaultStorjSatellite;
    this.storjService.getSatellites().subscribe(v => {
      this.storjSatellites = v;
    }, err => this.dialog.connectionError(err));
    this.storjService.getAuthMethods().subscribe(v => {
      this.storjAuthMethods = v;
    }, err => this.dialog.connectionError(err));
  }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    this.storjAuthMethod = parts.get('--storj-auth-method') || '';
    this.storjSatellite = parts.get('--storj-satellite') || '';
    this.storjApiKey = parts.get('--storj-api-key') || '';
    this.storjSecret = parts.get('--storj-secret') || '';
    this.storjSecretVerify = parts.get('--storj-secret-verify') || '';
    this.storjSharedAccess = parts.get('--storj-shared-access') || '';
    this.storjBucket = parts.get('--storj-bucket') || '';
    this.storjFolder = parts.get('--storj-folder') || '';

    let nukeopts = ['--storj-auth-method', '--storj-satellite', '--storj-api-key', '--storj-secret', '--storj-secret-verify', '--storj-shared-access', '--storj-bucket', '--storj-folder'];
    for (let x of nukeopts) {
      parts.delete(x);
    }
  }

  buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {
      'storj-auth-method': this.storjAuthMethod,
      'storj-satellite': this.storjSatellite,
      'storj-api-key': this.storjApiKey,
      'storj-secret': this.storjSecret,
      'storj-shared-access': this.storjSharedAccess,
      'storj-bucket': this.storjBucket,
      'storj-folder': this.storjFolder
    };

    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    const valid = this.validate();
    if (!valid) {
      return undefined;
    }

    return this.convert.format('{0}://storj.io/config{1}',
      this.key,
      this.parser.encodeDictAsUrl(opts)
    );
  }

  extraConnectionTests(): boolean {
    return true;
  }

  protected validate(): boolean {
    let res = true;

    if (res && !this.storjAuthMethod) {
      res = this.editUri.requireField(this, 'storjAuthMethod', 'Authentication method');
    }

    if (res && this.storjAuthMethod == 'Access grant') {
      res = this.editUri.requireField(this, 'storjSharedAccess', 'storj_shared_access')
        && this.editUri.requireField(this, 'storjBucket', 'Bucket');
    }
    if (res && this.storjAuthMethod == 'API key') {
      res = this.editUri.requireField(this, 'storjApiKey', 'API key')
        && this.editUri.requireField(this, 'storjSecret', 'Encryption passphrase')
        && this.editUri.requireField(this, 'storjBucket', 'Bucket');
    }
    if (res && this.storjAuthMethod == 'API key' && !this.storjSatellite) {
      res = this.editUri.requireField(this, 'storjSatelliteCustom', 'Custom Satellite');
    }
    if (res && this.storjAuthMethod == 'API key' && this.storjSecret != this.storjSecretVerify) {
      this.dialog.dialog('Error', 'The encryption passphrases do not match');
      res = false;
    }
    let re = new RegExp('^([a-z0-9]+([a-z0-9\-][a-z0-9])*)+(.[a-z0-9]+([a-z0-9\-][a-z0-9])*)*$');
    if (res && (this.storjBucket && !re.test(this.storjBucket) || !(this.storjBucket.length > 2 && this.storjBucket.length < 64))) {
      this.dialog.dialog('Error', 'Bucket name can only be between 3 and 63 characters long and contain only lower-case characters, numbers, periods and dashes');
      res = false;
    }
    return res;
  }
  isCustomSatellite() {
    return !Object.values(this.storjSatellites).includes(this.storjSatellite);
  }
}
