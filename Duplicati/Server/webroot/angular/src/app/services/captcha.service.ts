import { HttpClient } from '@angular/common/http';
import { Call } from '@angular/compiler';
import { Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { CaptchaComponent } from '../dialog-templates/captcha/captcha.component';
import { DialogService } from './dialog.service';
import { UrlService } from './url.service';

export interface CallbackData {
  message: string;
  target: string;
  callback: (token: string, answer: string) => void;
  attempts: number;
  hasfailed: boolean;
  verifying: boolean;
  token?: string;
  answer?: string;
}

@Injectable({
  providedIn: 'root'
})
export class CaptchaService {

  private _active?: CallbackData;
  get active(): CallbackData | undefined {
    return this._active;
  }
  private set active(v: CallbackData | undefined) {
    this._active = v;
  }
  private attemptSolve?: () => void;

  constructor(private dialog: DialogService,
    private url: UrlService,
    private client: HttpClient) { }

  authorize(title: string, message: string, target: string, callback: (token: string, answer: string) => void) {
    let cb: CallbackData = this.active = {
      message: message,
      target: target,
      callback: callback,
      attempts: 0,
      hasfailed: false,
      verifying: false
    };

    this.attemptSolve = () => {
      if (cb.attempts >= 3) {
        cb.attempts = 0;
        cb.token = undefined;
      }

      this.dialog.htmlDialog(title, CaptchaComponent, ['Cancel', 'OK'], btn => {
        if (btn != 1) {
          this.active = undefined;
          return;
        }

        cb.attempts += 1;
        cb.verifying = true;

        this.dialog.dialog('Verifying answer', 'Verifying ...', [], undefined, () => {
          let formData = new FormData();
          formData.set('target', cb.target);
          formData.set('answer', cb.answer || '');
          this.client.post<void>(`/captcha/${encodeURIComponent(cb.token || '')}`, formData).subscribe(
            () => {
              this.dialog.dismissCurrent();
              this.active = undefined;
              cb.callback(cb.token || '', cb.answer || '');
            }, err => {
              this.dialog.dismissCurrent();
              cb.verifying = false;
              cb.hasfailed = true;
              if ((err.status == 400 || err.status == 403) && this.attemptSolve) {
                this.attemptSolve();
              } else {
                this.dialog.connectionError('', err);
              }
            }
          );
        });
      });

    };

    this.attemptSolve();
  }

  generate(target: string): Observable<string> {
    let formData = new FormData();
    formData.set('target', target);
    return this.client.post<{ token: string }>('/captcha', formData).pipe(map(v => v.token));
  }

  getImageUrl(token: string): string {
    return `${this.url.getAPIPrefix()}/captcha/${token}`;
  }
}
