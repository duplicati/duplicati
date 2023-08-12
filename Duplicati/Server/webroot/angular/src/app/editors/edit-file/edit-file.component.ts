import { Component, EventEmitter, Input, Output } from '@angular/core';
import { BackendEditorComponent } from '../backend-editor.component';

@Component({
  templateUrl: './edit-file.component.html',
  styleUrls: ['./edit-file.component.less']
})
export class EditFileComponent implements BackendEditorComponent {
  @Input() path: string = '';
  @Output() pathChange = new EventEmitter<string>();
  @Input() username: string = '';
  @Output() usernameChange = new EventEmitter<string>();
  @Input() password: string = '';
  @Output() passwordChange = new EventEmitter<string>();

  hideFolderBrowser: boolean = false;
  showHiddenFolders: boolean = false;

  ngOnInit() {
    this.hideFolderBrowser = this.path != '';
  }
}
