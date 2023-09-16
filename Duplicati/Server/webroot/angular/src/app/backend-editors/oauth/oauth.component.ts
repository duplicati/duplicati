import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { EmptyError } from 'rxjs';
import { Subscription } from 'rxjs';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { OauthService } from '../services/oauth.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, BACKEND_SUPPORTS_SSL, CommonBackendData } from '../../backend-editor';

@Component({
  selector: 'app-editor-oauth',
  templateUrl: './oauth.component.html',
  styleUrls: ['./oauth.component.less']
})
export class OauthComponent implements BackendEditorComponent {
  @Input({ required: true }) commonData!: CommonBackendData;
  @Output() commonDataChange = new EventEmitter<CommonBackendData>();

  oauthCreateToken: string = '';
  oauthStartLink: string = '';
  oauthInProgress: boolean = false;
  authID: string = '';

  get path() {
    return this.commonData.path || '';
  }
  set path(v) {
    this.commonData.path = v;
    this.commonDataChange.emit(this.commonData);
  }

  private oauthSubscription?: Subscription;

  constructor(@Inject(BACKEND_KEY) protected key: string,
    @Inject(BACKEND_SUPPORTS_SSL) protected supportsSSL: boolean,
    protected editUri: EditUriService,
    protected dialog: DialogService,
    private oauthService: OauthService,
    protected parser: ParserService) { }


  ngOnInit() {
    ({ token: this.oauthCreateToken, startLink: this.oauthStartLink } = this.oauthService.generateToken(this.key));
  }

  ngOnDestroy() {
    this.oauthSubscription?.unsubscribe();
  }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    if (parts.get('--authid')) {
      data.username = parts.get('--authid');
    }
    parts.delete('--authid');
    this.editUri.mergeServerAndPath(data);
  }

  buildUri(advancedOptions: string[]): string | undefined {
    let opts: Record<string, string> = {
      authid: this.authID
    };
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    const valid = this.validate();
    if (!valid) {
      return undefined;
    }

    return `${this.key}${this.supportsSSL && this.commonData.useSSL ? 's' : ''}://${this.commonData.path || ''}${this.parser.encodeDictAsUrl(opts)}`;
  }

  extraConnectionTests(): boolean {
    return true;
  }

  oauthStartTokenCreation() {
    this.oauthInProgress = true;
    this.oauthSubscription = this.oauthService.showTokenWindow(this.oauthCreateToken, this.oauthStartLink).subscribe(
      authid => {
        this.authID = authid;
        this.oauthInProgress = false;
      }, err => {
        this.oauthInProgress = false;
        if (!(err instanceof EmptyError)) {
          this.dialog.connectionError('Failed to connect: ', err);
        }
      }
    );
  }

  protected validate(): boolean {
    return this.editUri.requireField(this, 'authID', 'AuthID')
      && this.editUri.recommendField(this.commonData, 'path',
        'If you do not enter a path, all files will be stored in the login folder.\nAre you sure this is what you want?');
  }
}
