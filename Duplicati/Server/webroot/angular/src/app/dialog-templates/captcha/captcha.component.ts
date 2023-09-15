import { Component, Input } from '@angular/core';
import { Subscription } from 'rxjs';
import { CallbackData, CaptchaService } from '../../services/captcha.service';
import { DialogConfig, DialogTemplate } from '../../services/dialog-config';
import { DialogService } from '../../services/dialog.service';

@Component({
  selector: 'app-dialog-captcha',
  templateUrl: './captcha.component.html',
  styleUrls: ['./captcha.component.less']
})
export class CaptchaComponent implements DialogTemplate {
  @Input() config: DialogConfig | undefined;

  entry?: CallbackData;
  imageurl?: string;

  private subscription?: Subscription;
  constructor(private captcha: CaptchaService,
    private dialog: DialogService) { }

  ngOnInit() {
    this.entry = this.captcha.active;
    if (this.entry?.token == null) {
      this.reload();
    } else {
      this.imageurl = this.captcha.getImageUrl(this.entry.token);
    }
  }

  reload() {
    if (!this.entry) {
      return;
    }
    this.imageurl = undefined;
    this.subscription?.unsubscribe();
    this.subscription = this.captcha.generate(this.entry.target).subscribe(token => {
      this.entry!.token = token;
      this.imageurl = this.captcha.getImageUrl(token);
    }, err => {
      this.dialog.dismissCurrent();
      this.dialog.connectionError('Failed to connect: ', err);
    });
  }
}
