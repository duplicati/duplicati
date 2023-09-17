import { Component, EventEmitter, Inject, Input, Output } from '@angular/core';
import { DialogService } from '../../services/dialog.service';
import { EditUriService } from '../../services/edit-uri.service';
import { ParserService } from '../../services/parser.service';
import { BackendEditorComponent, BACKEND_KEY, CommonBackendData } from '../../backend-editor';
import { EMPTY, Observable } from 'rxjs';
import { of } from 'rxjs';

@Component({
  selector: 'app-editor-rclone',
  templateUrl: './rclone.component.html',
  styleUrls: ['./rclone.component.less']
})
export class RcloneComponent implements BackendEditorComponent {
  @Input({ required: true }) commonData!: CommonBackendData;
  @Output() commonDataChange = new EventEmitter<CommonBackendData>();

  get path() {
    return this.commonData.path || '';
  }
  set path(v) {
    this.commonData.path = v;
    this.commonDataChange.emit(this.commonData);
  }
  get server() {
    return this.commonData.server || '';
  }
  set server(v) {
    this.commonData.server = v;
    this.commonDataChange.emit(this.commonData);
  }

  localRepository: string = '';
  remoteRepository: string = '';
  remotePath: string = '';

  constructor(@Inject(BACKEND_KEY) private key: string,
    private editUri: EditUriService,
    private dialog: DialogService,
    private parser: ParserService) { }

  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void {
    this.localRepository = parts.get('--rclone-local-repository') ?? '';
    //this.remoteRepository = parts.get('--rclone-remote-repository') ?? '';
    //this.remotePath = parts.get('--rclone-remote-path') ?? '';

    let nukeopts = ['--rclone-option', '--rclone-executable', '--rclone-local-repository'];
    for (let x of nukeopts) {
      parts.delete(x);
    }
  }

  buildUri(advancedOptions: string[]): Observable<string> {
    let opts: Record<string, string> = {
      'rclone-local-repository': this.localRepository,
      //'rclone-option': this.rcloneOption,
      //'rclone-executable': this.rcloneExecutable
    };
    if ((opts['rclone-executable'] || '') == '')
      delete opts['rclone-executable'];
    if ((opts['rclone-option'] || '') == '')
      delete opts['rclone-option'];
    this.editUri.mergeAdvancedOptions(this.commonData, advancedOptions, opts);

    const valid = this.validate();
    if (!valid) {
      return EMPTY;
    }

    return of(`${this.key}://${this.commonData.server || ''}/${this.commonData.path || ''}${this.parser.encodeDictAsUrl(opts)}`);
  }

  private validate(): boolean {
    let res = this.editUri.requireField(this, 'localRepository', $localize`Local Repository`)
      && this.editUri.requireField(this.commonData, 'path', $localize`Remote Path`)
      && this.editUri.requireField(this.commonData, 'server', $localize`Remote Repository`);

    if (res) {
      let bucketname = this.commonData.server || '';
      let ix = bucketname.search(/[^A-Za-z0-9-]/g);

      if (ix >= 0) {
        this.dialog.dialog($localize`Error`, $localize`The 'Bucket Name' contains an invalid character: ${bucketname[ix]} (value: ${bucketname.charCodeAt(ix)}, index: ${ix})`);
        res = false;
      }
    }
    if (res) {
      let pathname = this.commonData.path || '';
      for (var i = pathname.length - 1; i >= 0; i--) {
        var char = pathname.charCodeAt(i);

        if (char == '\\'.charCodeAt(0) || char == 127 || char < 32) {
          this.dialog.dialog($localize`Error`, $localize`The 'Path' field contains an invalid character: ${pathname[i]} (value: ${char}, index: ${i})`);
          res = false;
          break;
        }
      }
    }

    return res;
  }
  extraConnectionTests(): Observable<boolean> {
    return of(true);
  }
}

