import { KeyValue } from '@angular/common';
import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { DEFAULT_OPENSTACK_SERVER, DEFAULT_OPENSTACK_VERSION, OpenstackService } from '../services/openstack.service';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, CommonBackendData } from '../../backend-editor';
import { EMPTY, Observable } from 'rxjs';
import { of } from 'rxjs';

@Component({
  selector: 'app-editor-openstack',
  templateUrl: './openstack.component.html',
  styleUrls: ['./openstack.component.less']
})
export class OpenstackComponent implements BackendEditorComponent {
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

  openstackServer?: string | null;
  openstackServerCustom?: string;
  openstackVersion?: string | null;
  openstackProviders: Record<string, string | null> = {};
  openstackVersions: Record<string, string | null> = {};
  openstackDomainname: string = '';
  openstackTenantname: string = '';
  openstackApiKey: string = '';
  openstackRegion: string = '';

  constructor(@Inject(BACKEND_KEY) private key: string,
    private openstack: OpenstackService,
    @Inject(DEFAULT_OPENSTACK_SERVER) private defaultServer: string,
    @Inject(DEFAULT_OPENSTACK_VERSION) private defaultVersion: string,
    private editUri: EditUriService,
    private dialog: DialogService,
    private parser: ParserService) { }

  ngOnInit() {
    this.openstack.getProviders().subscribe(v => {
      this.openstackProviders = v;
      if (this.openstackServer === undefined && this.openstackServerCustom == null) {
        this.openstackServer = this.defaultServer;
      }
    }, this.dialog.connectionError);
    this.openstack.getVersions().subscribe(v => {
      this.openstackVersions = v;
      if (this.openstackVersion === undefined) {
        this.openstackVersion = this.defaultVersion;
      }
    }, this.dialog.connectionError);
  }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    this.openstackDomainname = parts.get('--openstack-domain-name') || '';
    this.openstackServer = this.openstackServerCustom = parts.get('--openstack-authuri');
    this.openstackVersion = parts.get('--openstack-version');
    this.openstackTenantname = parts.get('--openstack-tenant-name') || '';
    this.openstackApiKey = parts.get('--openstack-apikey') || '';
    this.openstackRegion = parts.get('--openstack-region') || '';

    let nukeopts = ['--openstack-domain-name', '--openstack-authuri', '--openstack-tenant-name', '--openstack-apikey', '--openstack-region', '--openstack-version'];
    for (let x of nukeopts) {
      parts.delete(x);
    }

    this.editUri.mergeServerAndPath(data);
  }

  buildUri(advancedOptions: string[]): Observable<string> {
    let opts: Record<string, string> = {
      'openstack-domain-name': this.openstackDomainname,
      'openstack-authuri': (this.isCustomServer() ? this.openstackServerCustom : this.openstackServer) ?? '',
      'openstack-version': this.openstackVersion ?? '',
      'openstack-tenant-name': this.openstackTenantname,
      'openstack-apikey': this.openstackApiKey,
      'openstack-region': this.openstackRegion

    };

    if ((opts['openstack-domain-name'] || '') == '')
      delete opts['openstack-domain-name'];
    if ((opts['openstack-tenant-name'] || '') == '')
      delete opts['openstack-tenant-name'];
    if ((opts['openstack-apikey'] || '') == '')
      delete opts['openstack-apikey'];
    if ((opts['openstack-region'] || '') == '')
      delete opts['openstack-region'];
    if ((opts['openstack-version'] || '') == '')
      delete opts['openstack-version'];
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    const valid = this.validate();
    if (!valid) {
      return EMPTY;
    }

    return of(`${this.key}://${this.commonData.path || ''}${this.parser.encodeDictAsUrl(opts)}`);
  }

  private validate(): boolean {
    let res = this.editUri.requireField(this.commonData, 'username', $localize`Username`)
      && this.editUri.requireField(this.commonData, 'path', $localize`Bucket Name`);
    if (res && (this.openstackServer || '').trim() == '' && (this.openstackServerCustom || '').trim() == '') {
      this.dialog.dialog($localize`Error`, $localize`You must select or fill in the AuthURI`);
      res = false;
    }

    if (this.openstackVersion?.trim() == 'v3') {
      if (res && this.password.trim().length == 0) {
        this.dialog.dialog($localize`Error`, $localize`You must enter a password to use v3 API`);
        res = false;
      }
      if (res && this.openstackDomainname.trim().length == 0) {
        this.dialog.dialog($localize`Error`, $localize`You must enter a domain name to use v3 API`);
        res = false;
      }
      if (res && this.openstackTenantname.trim().length == 0) {
        this.dialog.dialog($localize`Error`, $localize`You must enter a tenant (aka project) name to use v3 API`);
        res = false;
      }
      if (res && this.openstackApiKey.length != 0) {
        this.dialog.dialog($localize`Error`, $localize`Openstack API Key are not supported in v3 keystone API.`);
        res = false;
      }
    } else {
      if ((this.openstackApiKey || '').trim().length == 0) {
        if (res && this.password.trim().length == 0) {
          this.dialog.dialog($localize`Error`, $localize`You must enter either a password or an API Key`);
          res = false;
        }
        if (res && this.openstackTenantname.trim().length == 0) {
          this.dialog.dialog($localize`Error`, $localize`You must enter a tenant name if you do not provide an API Key`);
          res = false;
        }
      } else {
        if (res && this.password.trim().length != 0) {
          this.dialog.dialog($localize`Error`, $localize`You must enter either a password or an API Key, not both`);
          res = false;
        }
      }
    }

    return res;
  }

  extraConnectionTests(): Observable<boolean> {
    return of(true);
  }

  compareValue(a: KeyValue<string, string | null>, b: KeyValue<string, string | null>) {
    if ((a.value || '') < (b.value || '')) {
      return -1;
    } else if (a.value == b.value) {
      return 0;
    }
    return 1;
  }

  isCustomServer(): boolean {
    return !Object.values(this.openstackProviders).includes(this.openstackServer ?? null);
  }
}
