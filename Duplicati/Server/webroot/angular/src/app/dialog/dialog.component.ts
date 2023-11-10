import { ComponentRef, Type, ViewChild } from '@angular/core';
import { Component, TemplateRef } from '@angular/core';
import { Subscription } from 'rxjs';
import { DynamicHostDirective } from '../directives/dynamic-host.directive';
import { DialogConfig, DialogTemplate } from '../services/dialog-config';
import { DialogService } from '../services/dialog.service';

@Component({
  selector: 'app-dialog',
  templateUrl: './dialog.component.html',
  styleUrls: ['./dialog.component.less']
})
export class DialogComponent {

  buttonTemplate?: Type<DialogTemplate>;
  htmlTemplate?: Type<DialogTemplate>;

  currentItem?: DialogConfig;

  htmlInstance?: ComponentRef<DialogTemplate>;
  buttonInstance?: ComponentRef<DialogTemplate>;

  private subscription?: Subscription;

  constructor(public dialogService: DialogService) { }

  ngOnInit() {
    this.subscription = this.dialogService.currentItem.subscribe(c => {
      this.currentItem = c;
      this.updateItem();
    });
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  updateItem() {
    this.htmlTemplate = this.currentItem?.htmltemplate;
    this.buttonTemplate = this.currentItem?.buttonTemplate;
    this.buttonInstance?.setInput('config', this.currentItem);
    this.htmlInstance?.setInput('config', this.currentItem);
  }

  trackByIndex(index: number, item: string) {
    return index;
  }

  onButtonClick(index: number) {
    if (!this.currentItem) {
      return;
    }
    let cur = this.currentItem;
    let input = this.currentItem.textarea;
    this.dialogService.dismissCurrent(false);

    if (cur.callback) {
      cur.callback(index, input, cur);
    }
    cur.subject.next({ event: 'button', input: input, buttonIndex: index, config: cur });
    cur.subject.complete();
  }

  initHtmlTemplate(ref: ComponentRef<unknown> | undefined) {
    this.htmlInstance = ref as ComponentRef<DialogTemplate>;
    this.htmlInstance.setInput('config', this.currentItem);
  }
  initButtonTemplate(ref: ComponentRef<unknown> | undefined) {
    this.buttonInstance = ref as ComponentRef<DialogTemplate>;
    this.buttonInstance.setInput('config', this.currentItem);
  }
}
