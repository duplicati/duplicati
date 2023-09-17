import { TemplateRef, ViewChild } from '@angular/core';
import { ElementRef } from '@angular/core';
import { Component, Input } from '@angular/core';
import { ClipboardParams, IClipboardResponse } from 'ngx-clipboard';
import { DialogConfig, DialogTemplate } from '../../services/dialog-config';

@Component({
  selector: 'app-dialog-copy-clipboard',
  templateUrl: './copy-clipboard-buttons.component.html',
  styleUrls: ['./copy-clipboard-buttons.component.less']
})
export class CopyClipboardButtonsComponent implements DialogTemplate {
  @Input() config: DialogConfig | undefined;
  @ViewChild('tooltipTarget', { static: true })
  target!: ElementRef<HTMLElement>;

  showTooltip(msg: string) {
    this.target.nativeElement.addEventListener('mouseleave', e => {
      this.target.nativeElement.setAttribute('class', 'button');
      this.target.nativeElement.removeAttribute('aria-label');
    });

    this.target.nativeElement.setAttribute('class', 'button tooltipped tooltipped-w');
    this.target.nativeElement.setAttribute('aria-label', msg);
  }

  onCopySuccess(event: IClipboardResponse) {
    // clear selection
    this.showTooltip($localize`Copied!`);
  }
  onCopyError(event: IClipboardResponse) {
    this.showTooltip($localize`Copy failed. Please manually copy the URL`);
  }
}
