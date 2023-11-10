import { Injectable, InjectionToken, Type } from '@angular/core';
import { ReplaySubject } from 'rxjs';
import { BehaviorSubject, defer, EMPTY, Observable, Subject } from 'rxjs';
import { DialogCallback, DialogConfig, DialogEvent, DialogState, DialogTemplate } from './dialog-config';

@Injectable({
  providedIn: 'root'
})
export class DialogService {

  state: DialogState = {
    CurrentItem: new BehaviorSubject<DialogConfig | undefined>(undefined),
    Queue: []
  };


  get currentItem(): Observable<DialogConfig | undefined> {
    return this.state.CurrentItem;
  }

  constructor() { }

  alert(message: string): DialogConfig | undefined {
    return this.enqueueDialog({ message: message });
  }

  confirm(message: string, callback: DialogCallback): DialogConfig | undefined {
    return this.enqueueDialog({
      message: message,
      callback: callback,
      buttons: [$localize`Cancel`, $localize`OK`]
    });
  }

  accept(message: string, callback: DialogCallback): DialogConfig | undefined {
    return this.enqueueDialog({
      message: message,
      callback: callback,
      buttons: [$localize`OK`]
    });
  }

  dialog(title: string, message: string, buttons?: string[], callback?: DialogCallback, onshow?: () => void): DialogConfig | undefined {
    return this.enqueueDialog({
      message: message,
      title: title,
      callback: callback,
      buttons: buttons,
      onshow: onshow
    });
  }

  dialogObservable(title: string, message: string, buttons?: string[]): Observable<DialogEvent> {
    return defer(() => this.enqueueDialog({
      message: message,
      title: title,
      buttons: buttons,
    })?.subject.asObservable() ?? EMPTY);
  }

  htmlDialog(title: string, htmltemplate: Type<DialogTemplate>, buttons: string[], callback: DialogCallback, onshow?: () => void): DialogConfig | undefined {
    return this.enqueueDialog({
      htmltemplate: htmltemplate,
      title: title,
      callback: callback,
      buttons: buttons,
      onshow: onshow
    });
  }

  textareaDialog(title: string, message: string, placeholder: string | undefined, textarea: string,
    buttons: string[], buttonTemplate: Type<DialogTemplate> | undefined, callback?: DialogCallback, onshow?: () => void): DialogConfig | undefined {
    return this.enqueueDialog({
      enableTextarea: true,
      title: title,
      message: message,
      placeholder: placeholder,
      textarea: textarea,
      callback: callback,
      buttons: buttons,
      buttonTemplate: buttonTemplate,
      onshow: onshow
    });
  }

  enqueueDialog(config: Partial<DialogConfig>): DialogConfig | undefined {
    if (config.message == null && config.htmltemplate == null && config.enableTextarea == null) {
      return undefined;
    }
    config.title = config.title || $localize`Information`;
    if (config.buttons == null) {
      config.buttons = [$localize`OK`];
    }

    config.dismiss = () => {
      if (this.state.CurrentItem.value === config) {
        this.dismissCurrent();
      } else {
        config.subject?.complete();
      }
    };
    config.subject = new ReplaySubject(1);
    this.state.Queue.push(config as DialogConfig);
    if (this.state.CurrentItem.value == null) {
      this.dismissCurrent();
    }
    return config as DialogConfig;
  }

  dismissCurrent(complete?: boolean): void {
    if (this.state.CurrentItem.value != null) {
      const config = this.state.CurrentItem.value!;
      if (config.ondismiss) {
        config.ondismiss();
      }
      config.subject.next({ event: 'dismiss', config: config });
      if (complete !== false) {
        config.subject.complete();
      }

      this.state.CurrentItem.next(undefined);
    }

    while (this.state.Queue.length > 0) {
      this.state.CurrentItem.next(this.state.Queue[0]);
      const config = this.state.Queue.shift()!;
      if (config.subject.closed) {
        continue;
      }

      if (config.onshow) {
        config.onshow();
      }
      config.subject.next({ event: 'show', config: config });
    }
  }

  dismissAll(): void {
    while (this.state.CurrentItem.value != null) {
      this.dismissCurrent();
    }
  }

  notifyInputError(msg: string) {
    return this.dialog($localize`Error`, msg);
  }

  connectionError(txt: string): ((msg: string | any) => void);
  connectionError(txt: any): void;
  connectionError(txt: string | any, msg: any): void;
  connectionError(txt: string | any, msg?: string | any): ((msg: string | any) => void) | void {
    if (typeof txt === 'string') {
      if (msg == null)
        return (msg) => {
          if (msg && msg.error && msg.error.Message)
            this.dialog($localize`Error`, txt + msg.error.Message);
          else
            this.dialog($localize`Error`, txt + msg.statusText);
        };
    } else {
      msg = txt;
      txt = '';
    }

    if (msg && msg.error && msg.error.Message)
      this.dialog($localize`Error`, txt + msg.error.Message);
    else if (msg.statusText)
      this.dialog($localize`Error`, txt + msg.statusText);
    else
      this.dialog($localize`Error`, txt + msg);
  }
}
