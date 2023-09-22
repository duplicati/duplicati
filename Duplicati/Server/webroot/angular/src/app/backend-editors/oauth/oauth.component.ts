import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { EmptyError, map } from 'rxjs';
import { Subscription } from 'rxjs';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { OauthService } from '../services/oauth.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, BACKEND_SUPPORTS_SSL, CommonBackendData } from '../../backend-editor';
import { Observable } from 'rxjs';
import { EMPTY } from 'rxjs';
import { of } from 'rxjs';
import { filter } from 'rxjs';

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

  buildUri(advancedOptions: string[]): Observable<string> {
    let opts: Record<string, string> = {
      authid: this.authID
    };
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    return this.validate().pipe(filter(v => v),
      map(() => `${this.key}${this.supportsSSL && this.commonData.useSSL ? 's' : ''}://${this.commonData.path || ''}${this.parser.encodeDictAsUrl(opts)}`));
  }

  extraConnectionTests(): Observable<boolean> {
    return of(true);
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
          this.dialog.connectionError($localize`Failed to connect: `, err);
        }
      }
    );
  }

  protected validate(): Observable<boolean> {
    if (this.editUri.requireField(this, 'authID', $localize`AuthID`)) {
      return this.editUri.recommendPath(this.commonData);
    }
    return of(false);
  }
}
