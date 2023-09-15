import { TemplateRef, Type } from "@angular/core";
import { BehaviorSubject } from "rxjs";

export interface DialogCallback {
  (buttonIndex: number, input: string | undefined, dialog: DialogConfig): void;
}

export interface DialogTemplate {
  config: DialogConfig | undefined;
}

export interface DialogConfig {
  message?: string;
  callback?: DialogCallback;
  title?: string;
  buttons?: string[];
  dismiss?: () => void;
  onshow?: () => void;
  ondismiss?: () => void;

  enableTextarea?: boolean;
  placeholder?: string;
  textarea?: string;
  // Data can be set by templates
  data?: any;

  htmltemplate?: Type<DialogTemplate>;
  buttonTemplate?: Type<DialogTemplate>;
}

export interface DialogState {
  CurrentItem: BehaviorSubject<DialogConfig | undefined>;
  Queue: DialogConfig[];
}
