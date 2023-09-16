import { Component } from '@angular/core';
import { of } from 'rxjs';
import { EMPTY, Observable } from 'rxjs';
import { CommonBackendData } from '../../backend-editor';
import { OauthComponent } from '../oauth/oauth.component';

@Component({
  selector: 'app-editor-msgroup',
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

  override buildUri(advancedOptions: string[]): Observable<string> {
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
      return EMPTY;
    }

    return of(`${this.key}://${this.commonData.path || ''}${this.parser.encodeDictAsUrl(opts)}`);
  }

  override validate(): boolean {
    return super.validate()
      && this.editUri.recommendField(this, 'groupEmail',
      'You should fill in ' + 'Group email' + ' unless you are explicitly spefifying --group-id');
  }
}
