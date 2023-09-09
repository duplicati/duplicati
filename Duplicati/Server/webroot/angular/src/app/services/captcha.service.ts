import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { DialogService } from './dialog.service';

interface CallbackData {
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

  private active?: CallbackData;
  private attemptSolve?: () => void;

  constructor(private dialog: DialogService,
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

      this.dialog.htmlDialog(title, 'templates/captcha.html', ['Cancel', 'OK'], btn => {
        if (btn != 1) {
          this.active = undefined;
          return;
        }

        cb.attempts += 1;
        cb.verifying = true;

        this.dialog.dialog('Verifying answer', 'Verifying ...', [], undefined, () => {
          this.client.post<void>(`/captcha/${encodeURIComponent(cb.token || '')}`, { answer: cb.answer, target: cb.target }).subscribe(
            () => {
              this.dialog.dismissCurrent();
              this.active = undefined;
              cb.callback(cb.token || '', cb.answer || '');
            }, err => {
              this.dialog.dismissCurrent();
              cb.verifying = false;
              cb.hasfailed = true;
              if (err.status == 400 && this.attemptSolve) {
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
}
