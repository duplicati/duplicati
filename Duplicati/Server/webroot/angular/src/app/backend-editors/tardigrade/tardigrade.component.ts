import { Component } from '@angular/core';
import { CommonBackendData } from '../../backend-editor';
import { StorjComponent } from '../storj/storj.component';

// this is only for backwards compatibility
@Component({
  selector: 'app-editor-tardigrade',
  templateUrl: './tardigrade.component.html',
  styleUrls: ['./tardigrade.component.less']
})
export class TardigradeComponent extends StorjComponent {

  override parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    this.storjAuthMethod = parts.get('--tardigrade-auth-method') || '';
    this.storjSatellite = parts.get('--tardigrade-satellite') || '';
    this.storjApiKey = parts.get('--tardigrade-api-key') || '';
    this.storjSecret = parts.get('--tardigrade-secret') || '';
    this.storjSecretVerify = parts.get('--tardigrade-secret-verify') || '';
    this.storjSharedAccess = parts.get('--tardigrade-shared-access') || '';
    this.storjBucket = parts.get('--tardigrade-bucket') || '';
    this.storjFolder = parts.get('--tardigrade-folder') || '';

    let nukeopts = ['--tardigrade-auth-method', '--tardigrade-satellite', '--tardigrade-api-key', '--tardigrade-secret', '--tardigrade-secret-verify', '--tardigrade-shared-access', '--tardigrade-bucket', '--tardigrade-folder'];
    for (let x of nukeopts) {
      parts.delete(x);
    }
  }

  override buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {
      'tardigrade-auth-method': this.storjAuthMethod,
      'tardigrade-satellite': this.storjSatellite,
      'tardigrade-api-key': this.storjApiKey,
      'tardigrade-secret': this.storjSecret,
      'tardigrade-shared-access': this.storjSharedAccess,
      'tardigrade-bucket': this.storjBucket,
      'tardigrade-folder': this.storjFolder
    };

    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    const valid = this.validate();
    if (!valid) {
      return undefined;
    }

    return this.convert.format('{0}://tardigrade.io/config{1}',
      this.key,
      this.parser.encodeDictAsUrl(opts)
    );
  }
}
