import { KeyValue } from '@angular/common';
import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { Subject } from 'rxjs';
import { Observable } from 'rxjs';
import { DEFAULT_S3_SERVER, S3Service } from '../../services/backends/s3.service';
import { ConvertService } from '../../services/convert.service';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, BACKEND_SUPPORTS_SSL, CommonBackendData } from '../backend-editor';

@Component({
  selector: 'app-s3',
  templateUrl: './s3.component.html',
  styleUrls: ['./s3.component.less']
})
export class S3Component implements BackendEditorComponent {
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
  get useSSL() {
    return this.commonData.useSSL || false;
  }
  set useSSL(v) {
    this.commonData.useSSL = v;
    this.commonDataChange.emit(this.commonData);
  }

  s3Server?: string;
  s3ServerCustom?: string;
  s3Region: string = '';
  s3RegionCustom: string = '';
  s3StorageClass: string = '';
  s3StorageClassCustom: string = '';
  s3Client: string = '';

  private s3BucketCheckName?: string;
  private s3BucketCheckUser?: string;

  s3Providers: Record<string, string | null> = {};
  s3Regions: Record<string, string | null> = {};
  s3StorageClasses: Record<string, string | null> = {};
  s3ClientOptions: ({ name: string, label: string })[] = [];


  constructor(@Inject(BACKEND_KEY) private key: string,
    @Inject(BACKEND_SUPPORTS_SSL) public supportsSSL: boolean,
    @Inject(DEFAULT_S3_SERVER) public defaultServer: string,
    private parser: ParserService,
    private convert: ConvertService,
    private s3Service: S3Service,
    private dialog: DialogService,
    private editUri: EditUriService) { }

  ngOnInit() {
    this.s3Service.getProviders().subscribe(v => {
      this.s3Providers = v;
      if (this.s3Server === undefined && this.s3ServerCustom == null) {
        this.s3Server = this.defaultServer;
      }
    },
      this.dialog.connectionError);
    this.s3Service.getRegions().subscribe(v => this.s3Regions = v,
      this.dialog.connectionError);
    this.s3Service.getStorageClasses().subscribe(v => this.s3StorageClasses = v,
      this.dialog.connectionError);
    this.s3ClientOptions = this.s3Service.clientOptions;
    this.s3Client = this.s3ClientOptions[0].name;
  }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    if (parts.get('--aws-access-key-id')) {
      data.username = parts.get('--aws-access-key-id') || '';
    } else if (parts.get('--aws_access_key_id')) {
      data.username = parts.get('--aws_access_key_id') || '';
    }
    if (parts.get('--aws-secret-access-key')) {
      data.password = parts.get('--aws-secret-access-key') || '';
    } else if (parts.get('--aws_secret_access_key')) {
      data.password = parts.get('--aws_secret_access_key') || '';
    }

    if (parts.get('--s3-use-rrs') && !parts.get('--s3-storage-class')) {
      parts.delete('--s3-use-rrs');
      parts.set('--s3-storage-class', 'REDUCED_REDUNDANCY');
    }

    this.s3Server = this.s3ServerCustom = parts.get('--s3-server-name') ?? '';
    this.s3Region = this.s3RegionCustom = parts.get('--s3-location-constraint') ?? '';

    const s3Client = parts.get('--s3-client');
    if (s3Client) {
      let idx = this.s3ClientOptions.findIndex(e => e.name == s3Client);
      this.s3Client = this.s3ClientOptions[idx].name;
    } else {
      this.s3Client = this.s3ClientOptions[0].name;
    }

    this.s3StorageClass = this.s3StorageClassCustom = parts.get('--s3-storage-class') ?? '';

    let nukeopts = ['--aws-access-key-id', '--aws-secret-access-key', '--aws_access_key_id', '--aws_secret_access_key', '--s3-use-rrs', '--s3-server-name', '--s3-location-constraint', '--s3-storage-class', '--s3-client'];
    for (let x of nukeopts) {
      parts.delete(x);
    }
  }

  buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {
      's3-server-name': (this.isCustomServer() ? this.s3ServerCustom : this.s3Server) ?? ''
    };
    if (this.isCustomRegion() && this.s3Region != null) {
      opts['s3-location-constraint'] = this.s3Region;
    } else if (this.isCustomRegion() && this.s3RegionCustom != null) {
      opts['s3-location-constraint'] = this.s3RegionCustom;
    }

    const storageClass = this.isCustomStorageClass() ? this.s3StorageClassCustom : this.s3StorageClass;
    if (storageClass != null) {
      opts['s3-storage-class'] = storageClass;
    }
    opts['s3-client'] = this.s3Client;

    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    const valid = this.validate();
    if (!valid) {
      return undefined;
    }

    return this.convert.format('{0}{1}://{2}/{3}{4}',
      this.key,
      (this.supportsSSL && this.commonData.useSSL) ? 's' : '',
      this.commonData.server || '',
      this.commonData.path || '',
      this.parser.encodeDictAsUrl(opts)
    );
  }

  extraConnectionTests(): boolean {
    let dlg = this.dialog.dialog('Testing permissions...', 'Testing permissions …', [], undefined, () => {
      this.s3Service.testPermissions(this.username, this.password).subscribe(res => {
        if (dlg?.dismiss) {
          dlg.dismiss();
        }

        if (res.isroot.toLowerCase() == 'true') {
          this.dialog.dialog('User has too many permissions',
            'The user has too many permissions. Do you want to create a new limited user, with only permissions to the selected path?',
            ['Cancel', 'No', 'Yes'],
            (ix) => {
              if (ix == 0 || ix == 1) {
                //callback();
              } else {
                this.directCreateIAMUser().subscribe(() => {
                  this.s3BucketCheckName = this.server;
                  this.s3BucketCheckUser = this.username;
                  //callback()
                });
              }
            });
        }
      }, err => {
        if (dlg?.dismiss) {
          dlg.dismiss();
        }
        this.dialog.connectionError(err);
      });
    });
    return true;
  }

  canGeneratePolicy(): boolean {
    return this.s3Service.canGeneratePolicy(this.s3Server);
  }

  directCreateIAMUser(): Observable<void> {
    let res = new Subject<void>();
    let dlg = this.dialog.dialog('Creating user...', 'Creating new user with limited access …', [], undefined, () => {
      let path = (this.server || '') + '/' + (this.path || '');
      this.s3Service.createIAMUser(path, this.username, this.password).subscribe(
        v => {
          this.username = v.accessid;
          this.password = v.secretkey;
          this.dialog.dialog('Created new limited user', `New user name is ${v.username}.\nUpdated credentials to use the new limited user`, ['OK']);
          res.next();
          res.complete();
        },
        err => {
          if (dlg?.dismiss) {
            dlg.dismiss();
          }
          this.dialog.connectionError(err);
          res.complete();
        }
      );
    });
    return res.asObservable();
  }
  createIAMPolicy() {
    if (this.validate()) {
      let path = (this.server || '') + '/' + (this.path || '');
      this.s3Service.getIAMPolicy(path).subscribe(
        v => this.dialog.dialog('AWS IAM Policy', v.doc),
        err => this.dialog.connectionError(err)
      );
    }
  }
  private validate(): boolean {
    let res = this.editUri.requireField(this.commonData, 'server', 'Bucket Name')
      && this.editUri.requireField(this.commonData, 'username', 'AWS Access ID')
      && this.editUri.requireField(this.commonData, 'password', 'AWS Access Key');

    if (res && (this.s3Server || '').trim().length == 0 && (this.s3ServerCustom || '').trim().length == 0) {
      this.dialog.dialog('Error', 'You must select or fill in the server');
      res = false;
    }

    if (res) {

      let checkUsernamePrefix = () => {
        if (!this.server.toLowerCase().startsWith(this.username.toLowerCase())
          && (this.s3BucketCheckName != this.server || this.s3BucketCheckUser != this.username)) {
          this.dialog.dialog('Adjust bucket name?',
            'The bucket name should start with your username, prepend automatically?',
            ['Cancel', 'No', 'Yes'], (ix) => {
              if (ix == 2)
                this.server = this.username.toLowerCase() + '-' + this.server;
              if (ix == 1 || ix == 2) {
                this.s3BucketCheckName = this.server;
                this.s3BucketCheckUser = this.username;
                //continuation();
              }
            });
        } else {
          //continuation();
        }
      }

      let checkLowerCase = () => {
        if (this.server.toLowerCase() != this.server) {
          this.dialog.dialog('Adjust bucket name?',
            'The bucket name should be all lower-case, convert automatically?',
            ['Cancel', 'No', 'Yes'], (ix) => {
              if (ix == 2)
                this.server = this.server.toLowerCase();

              if (ix == 1 || ix == 2)
                checkUsernamePrefix();
            });
        } else {
          checkUsernamePrefix();
        }
      };

      checkLowerCase();
    }
    return res;
  }

  compareValue(a: KeyValue<string, string | null>, b: KeyValue<string, string | null>) {
    if ((a.value || '') < (b.value || '')) {
      return -1;
    } else if (a.value == b.value) {
      return 0;
    }
    return 1;
  }
  isCustomRegion(): boolean {
    return !Object.values(this.s3Regions).includes(this.s3Region ?? null);
  }
  isCustomServer(): boolean {
    return !Object.values(this.s3Providers).includes(this.s3Server ?? null);
  }
  isCustomStorageClass(): boolean {
    return !Object.values(this.s3StorageClasses).includes(this.s3StorageClass ?? null);
  }
}
