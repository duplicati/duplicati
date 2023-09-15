import { Component } from '@angular/core';
import { CommonBackendData } from '../backend-editor';
import { OauthComponent } from '../oauth/oauth.component';

@Component({
  selector: 'app-msgroup',
  templateUrl: './msgroup.component.html',
  styleUrls: ['./msgroup.component.less']
})
export class MsgroupComponent extends OauthComponent {
  groupEmail: string = '';

  override parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    this.groupEmail = parts.get('--group-email') || '';
    parts.delete('--group-email');

    super.parseUriParts(data, parts);
  }

  override buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {
      'group-email': this.groupEmail,
      authid: this.authID
    };
    if (opts['group-email'] == '') {
      delete opts['group-email'];
    }
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    const valid = this.validate();
    if (!valid) {
      return undefined;
    }

    return `${this.key}://${this.commonData.path || ''}${this.parser.encodeDictAsUrl(opts)}`;
  }

  override validate(): boolean {
    return super.validate()
      && this.editUri.recommendField(this, 'groupEmail',
      'You should fill in ' + 'Group email' + ' unless you are explicitly spefifying --group-id');
  }
}
