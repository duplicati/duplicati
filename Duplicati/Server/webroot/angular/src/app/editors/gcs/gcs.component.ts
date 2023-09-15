import { KeyValue } from '@angular/common';
import { Component, Inject } from '@angular/core';
import { GcsService } from '../../services/backends/gcs.service';
import { OauthService } from '../../services/backends/oauth.service';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BACKEND_KEY, BACKEND_SUPPORTS_SSL, CommonBackendData } from '../backend-editor';
import { OauthComponent } from '../oauth/oauth.component';

@Component({
  selector: 'app-gcs',
  templateUrl: './gcs.component.html',
  styleUrls: ['./gcs.component.less']
})
export class GcsComponent extends OauthComponent {

  gcsLocations: Record<string, string | null> = {};
  gcsStorageClasses: Record<string, string | null> = {};
  gcsLocation: string | null = null;
  gcsLocationCustom: string = '';
  gcsStorageClass: string | null = null;
  gcsStorageClassCustom: string = '';
  gcsProjectId: string = '';

  constructor(@Inject(BACKEND_KEY) key: string,
    @Inject(BACKEND_SUPPORTS_SSL) supportsSSL: boolean,
    editUri: EditUriService,
    dialog: DialogService,
    oauthService: OauthService,
    parser: ParserService,
    private gcsService: GcsService) {
    super(key, supportsSSL, editUri, dialog, oauthService, parser);
  }

  override ngOnInit() {
    super.ngOnInit();
    this.gcsService.getLocations().subscribe(v => this.gcsLocations = v);
    this.gcsService.getStorageClasses().subscribe(v => this.gcsStorageClasses = v);
  }

  compareValue(a: KeyValue<string, string | null>, b: KeyValue<string, string | null>) {
    if ((a.value || '') < (b.value || '')) {
      return -1;
    } else if (a.value == b.value) {
      return 0;
    }
    return 1;
  }

  override parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    this.gcsLocation = parts.get('--gcs-location') ?? null;
    this.gcsLocationCustom = this.gcsLocation ?? '';
    this.gcsStorageClass = parts.get('--gcs-storage-class') ?? null;
    this.gcsStorageClassCustom = this.gcsStorageClass ?? '';
    this.gcsProjectId = parts.get('--gcs-project') ?? '';

    let nukeopts = ['--gcs-location', '--gcs-storage-class', '--gcs-project'];
    for (let n of nukeopts) {
      parts.delete(n);
    }

    super.parseUriParts(data, parts);
  }

  override buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {
      'gcs-location': this.isCustomLocation() ? this.gcsLocationCustom : this.gcsLocation ?? '',
      'gcs-storage-class': this.isCustomStorageClass() ? this.gcsStorageClassCustom : this.gcsStorageClass ?? '',
      authid: this.authID,
      'gcs-project': this.gcsProjectId
    };
    if (opts['gcs-location'] == '') {
      delete opts['gcs-location'];
    }
    if (opts['gcs-storage-class'] == '') {
      delete opts['gcs-storage-class'];
    }
    if (opts['gcs-project'] == '') {
      delete opts['gcs-project'];
    }
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    const valid = this.validate();
    if (!valid) {
      return undefined;
    }

    return `${this.key}://${this.commonData.path || ''}${this.parser.encodeDictAsUrl(opts)}`;
  }

  isCustomLocation(): boolean {
    return !Object.values(this.gcsLocations).includes(this.gcsLocation);
  }
  isCustomStorageClass(): boolean {
    return !Object.values(this.gcsStorageClasses).includes(this.gcsStorageClass);
  }
}
