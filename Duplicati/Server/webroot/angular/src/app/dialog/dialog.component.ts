import { Component, TemplateRef } from '@angular/core';
import { DialogService } from '../services/dialog.service';

@Component({
  selector: 'app-dialog',
  templateUrl: './dialog.component.html',
  styleUrls: ['./dialog.component.less']
})
export class DialogComponent {


  buttonTemplate?: TemplateRef<void>;
  htmlTemplate?: TemplateRef<void>;


  constructor(public dialogService: DialogService) { }

  trackByIndex(index: number, item: string) {
    return index;
  }

  onButtonClick(index: number) {
    let cur = this.dialogService.currentItem!;
    let input = cur.textarea;
    this.dialogService.dismissCurrent();

    if (cur.callback) {
      cur.callback(index, input, cur);
    }
  }
}
